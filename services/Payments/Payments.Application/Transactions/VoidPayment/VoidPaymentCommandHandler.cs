using FluentResults;
using Microsoft.Extensions.Logging;
using Payments.Application.Abstractions;
using Payments.Application.Common.Data;
using Payments.Domain.Errors;
using Payments.Domain.Transactions.ValueObjects;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Payments.Application.Transactions.VoidPayment;

/// <summary>
/// Handles <see cref="VoidPaymentCommand"/>. Calls
/// <see cref="IPaymentGateway.VoidAsync"/> and transitions an <c>Authorized</c> aggregate to
/// <c>Voided</c>. Aggregate FSM guards reject other source statuses with
/// <c>DataIntegrityException</c> (bug-class — saga should never request void on a
/// post-capture payment).
/// </summary>
internal sealed class VoidPaymentCommandHandler : ICommandHandler<VoidPaymentCommand>
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentGateway _gateway;
    private readonly ITransactionalOutbox<IPaymentsDbContext> _outbox;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<VoidPaymentCommandHandler> _logger;

    public VoidPaymentCommandHandler(
        IPaymentRepository repository,
        IPaymentGateway gateway,
        ITransactionalOutbox<IPaymentsDbContext> outbox,
        IDomainEventDispatcher dispatcher,
        TimeProvider timeProvider,
        ILogger<VoidPaymentCommandHandler> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _outbox = outbox;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(VoidPaymentCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tx = await _repository.GetByIdAsync(command.PaymentId, ct);
        if (tx is null)
        {
            return Result.Fail(PaymentsErrors.PaymentNotFound(command.PaymentId));
        }

        if (tx.Status == PaymentStatus.Voided)
        {
            _logger.LogInformation(
                "VoidPaymentCommand received for {PaymentId} already Voided; idempotent no-op.",
                tx.Id);
            return Result.Ok();
        }

        var gatewayTransactionId = tx.GatewayTransactionId
            ?? throw new InvalidOperationException(
                $"Payment {tx.Id} has no GatewayTransactionId despite status {tx.Status.Name}; this should be unreachable.");

        var gatewayResult = await _gateway.VoidAsync(gatewayTransactionId, ct);
        var utcNow = _timeProvider.GetUtcNow();

        if (gatewayResult.IsFailed)
        {
            _logger.LogWarning(
                "Gateway void call for {PaymentId} did not succeed; saga must retry.",
                tx.Id);
            return Result.Fail(PaymentsErrors.GatewayUnavailable());
        }

        var voidResult = tx.Void(gatewayResult.Value.ResponseCode, utcNow);
        if (voidResult.IsFailed)
        {
            return voidResult;
        }

        foreach (var domainEvent in tx.PopDomainEvents())
        {
            await _dispatcher.DispatchAsync(domainEvent, ct);
        }

        await _outbox.SaveChangesAsync(ct);

        return Result.Ok();
    }
}
