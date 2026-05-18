using FluentResults;
using Microsoft.Extensions.Logging;
using Payments.Application.Abstractions;
using Payments.Application.Common.Data;
using Payments.Domain.Errors;
using Payments.Domain.Transactions.ValueObjects;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Exceptions;

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

        // H-8: when an authorization token is on file, the wire AuthorizationId MUST match it.
        // Stale-token replays / saga bugs that pass the wrong id would otherwise contact the PSP
        // with the wrong token. If no token is yet stored (aggregate is Requested or pre-Authorize
        // Failed) the FSM pre-check below rejects the wrong-status case loudly.
        if (tx.GatewayTransactionId is not null
            && !string.Equals(tx.GatewayTransactionId, command.AuthorizationId, StringComparison.Ordinal))
        {
            throw new DataIntegrityException(
                "Payments.AuthorizationIdMismatch",
                $"Payment {tx.Id} stored GatewayTransactionId differs from wire AuthorizationId; saga bug or stale-token replay.");
        }

        if (tx.Status == PaymentStatus.Voided)
        {
            _logger.LogInformation(
                "VoidPaymentCommand received for {PaymentId} already Voided; idempotent no-op.",
                tx.Id);
            return Result.Ok();
        }

        // H-Cond-2: assert FSM transition is legal BEFORE touching the gateway. The aggregate's
        // own Void() method also guards this transition, but that guard fires AFTER the gateway
        // call — too late to prevent a real-world side-effect against a Stripe / Adyen adapter
        // when the saga sends Void against a post-capture aggregate.
        if (!tx.Status.CanTransitionTo(PaymentStatus.Voided))
        {
            throw new DataIntegrityException(
                "Payments.InvalidStatusTransition",
                $"Cannot void payment {tx.Id} from status '{tx.Status.Name}'.");
        }

        var gatewayTransactionId = tx.GatewayTransactionId
            ?? throw new DataIntegrityException(
                "Payments.MissingGatewayTransactionId",
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
