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

namespace Payments.Application.Transactions.RequestRefund;

/// <summary>
/// Handles <see cref="RequestRefundCommand"/>. Calls
/// <see cref="IPaymentGateway.RefundAsync"/> and transitions a <c>Captured</c>/<c>Completed</c>
/// aggregate to <c>Refunded</c>. Aggregate FSM guards reject other source statuses with
/// <c>DataIntegrityException</c> (bug-class — saga should never request refund on a
/// non-captured payment).
/// </summary>
internal sealed class RequestRefundCommandHandler : ICommandHandler<RequestRefundCommand>
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentGateway _gateway;
    private readonly ITransactionalOutbox<IPaymentsDbContext> _outbox;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RequestRefundCommandHandler> _logger;

    public RequestRefundCommandHandler(
        IPaymentRepository repository,
        IPaymentGateway gateway,
        ITransactionalOutbox<IPaymentsDbContext> outbox,
        IDomainEventDispatcher dispatcher,
        TimeProvider timeProvider,
        ILogger<RequestRefundCommandHandler> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _outbox = outbox;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(RequestRefundCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tx = await _repository.GetByIdForUpdateAsync(command.PaymentId, ct);
        if (tx is null)
        {
            return Result.Fail(PaymentsErrors.PaymentNotFound(command.PaymentId));
        }

        if (tx.Status == PaymentStatus.Refunded)
        {
            _logger.LogInformation(
                "RequestRefundCommand received for {PaymentId} already Refunded; idempotent no-op.",
                tx.Id);
            return Result.Ok();
        }

        // H-Cond-2: assert FSM transition is legal BEFORE touching the gateway. Without this
        // pre-check, a saga issuing Refund against an Authorized (not-yet-Captured) aggregate
        // would reach the real PSP before the aggregate's Refund() guard rejects the transition.
        if (!tx.Status.CanTransitionTo(PaymentStatus.Refunded))
        {
            throw new DataIntegrityException(
                "Payments.InvalidStatusTransition",
                $"Cannot refund payment {tx.Id} from status '{tx.Status.Name}'.");
        }

        // GatewayTransactionId is set by Authorize and is append-only per I-4. The
        // CanTransitionTo(Refunded) pre-check above proves the aggregate is in Captured or
        // Completed, so the bang here is safe — the aggregate's FSM is the single source of truth
        // for the invariant. The handler-level null-guard the closeout (#250) used to carry was
        // genuinely unreachable after the FSM pre-check landed and is removed.
        var gatewayTransactionId = tx.GatewayTransactionId!;

        var gatewayResult = await _gateway.RefundAsync(gatewayTransactionId, tx.Amount, command.Reason, ct);
        var utcNow = _timeProvider.GetUtcNow();

        if (gatewayResult.IsFailed)
        {
            // Refunds are reversal operations — declines here are infrastructure-class. Bubble up
            // for saga retry (gateway-side fraud / hold quirks become Path-B follow-ups).
            _logger.LogWarning(
                "Gateway refund call for {PaymentId} did not succeed; saga must retry.",
                tx.Id);
            return Result.Fail(PaymentsErrors.GatewayUnavailable());
        }

        var refundResult = tx.Refund(command.Reason, gatewayResult.Value.ResponseCode, utcNow);
        if (refundResult.IsFailed)
        {
            return refundResult;
        }

        foreach (var domainEvent in tx.PopDomainEvents())
        {
            await _dispatcher.DispatchAsync(domainEvent, ct);
        }

        await _outbox.SaveChangesAsync(ct);

        return Result.Ok();
    }
}
