using Ardalis.Specification.EntityFrameworkCore;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Payments.Application.Abstractions;
using Payments.Application.Common.Data;
using Payments.Domain.Errors;
using Payments.Domain.Transactions.Specifications;
using Payments.Domain.Transactions.ValueObjects;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Exceptions;

namespace Payments.Application.Transactions.CapturePayment;

/// <summary>
/// Handles <see cref="CapturePaymentCommand"/>. Calls
/// <see cref="IPaymentGateway.CaptureAsync"/> and transitions the aggregate
/// <c>Authorized → Captured → Completed</c> (v1 auto-completion). Gateway declines on capture
/// (rare but possible) move the aggregate to <c>Failed</c> via <c>MarkCaptureFailed</c>;
/// infrastructure errors return <c>GatewayUnavailable</c> for saga retry.
/// </summary>
internal sealed class CapturePaymentCommandHandler : ICommandHandler<CapturePaymentCommand>
{
    private readonly IPaymentsDbContext _dbContext;
    private readonly IPaymentGateway _gateway;
    private readonly ITransactionalOutbox<IPaymentsDbContext> _outbox;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CapturePaymentCommandHandler> _logger;

    public CapturePaymentCommandHandler(
        IPaymentsDbContext dbContext,
        IPaymentGateway gateway,
        ITransactionalOutbox<IPaymentsDbContext> outbox,
        IDomainEventDispatcher dispatcher,
        TimeProvider timeProvider,
        ILogger<CapturePaymentCommandHandler> logger)
    {
        _dbContext = dbContext;
        _gateway = gateway;
        _outbox = outbox;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(CapturePaymentCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Post-cross-cutting wave1-followup #255: the aggregate PK is the saga-issued
        // PaymentTransactionId, not the CorrelationId. AvroCapturePaymentCommand does not
        // carry PaymentTransactionId (only Authorize + RequestRefund do), so we resolve the
        // aggregate via the unique correlation_id index. command.PaymentId here is just the
        // log-scope echo from the mapper.
        var tx = await _dbContext.Transactions
            .WithSpecification(new PaymentByCorrelationIdSpec(command.CorrelationId))
            .FirstOrDefaultAsync(ct);
        if (tx is null)
        {
            return Result.Fail(PaymentsErrors.PaymentNotFound(command.PaymentId));
        }

        // H-8: when an authorization token is on file, the wire AuthorizationId MUST match it.
        // See the analogous comment in VoidPaymentCommandHandler.
        if (tx.GatewayTransactionId is not null
            && !string.Equals(tx.GatewayTransactionId, command.AuthorizationId, StringComparison.Ordinal))
        {
            throw new DataIntegrityException(
                "Payments.AuthorizationIdMismatch",
                $"Payment {tx.Id} stored GatewayTransactionId differs from wire AuthorizationId; saga bug or stale-token replay.");
        }

        if (tx.Status == PaymentStatus.Captured || tx.Status == PaymentStatus.Completed)
        {
            _logger.LogInformation(
                "CapturePaymentCommand received for {PaymentId} already in '{Status}'; idempotent no-op.",
                tx.Id,
                tx.Status.Name);
            return Result.Ok();
        }

        // H-Cond-2: assert FSM transition is legal BEFORE touching the gateway. Capture against
        // a Requested aggregate (saga skipped Authorize) or against a Voided/Failed aggregate
        // (saga ordering bug) would otherwise reach the real PSP before the aggregate's own
        // FSM guard fires.
        if (!tx.Status.CanTransitionTo(PaymentStatus.Captured))
        {
            throw new DataIntegrityException(
                "Payments.InvalidStatusTransition",
                $"Cannot capture payment {tx.Id} from status '{tx.Status.Name}'.");
        }

        // GatewayTransactionId is set by Authorize and is append-only per I-4. The
        // CanTransitionTo(Captured) pre-check above proves the aggregate is in Authorized, so the
        // bang here is safe — the aggregate's FSM is the single source of truth for the
        // invariant. The handler-level null-guard the closeout (#250) used to carry was genuinely
        // unreachable after the FSM pre-check landed and is removed.
        var gatewayTransactionId = tx.GatewayTransactionId!;

        var gatewayResult = await _gateway.CaptureAsync(gatewayTransactionId, tx.Amount, ct);
        var utcNow = _timeProvider.GetUtcNow();

        if (gatewayResult.IsSuccess)
        {
            var captureResult = tx.Capture(gatewayTransactionId, gatewayResult.Value.ResponseCode, utcNow);
            if (captureResult.IsFailed)
            {
                return captureResult;
            }
        }
        else
        {
            var declined = gatewayResult.Errors.OfType<GatewayDeclinedError>().FirstOrDefault();
            if (declined is null)
            {
                _logger.LogWarning(
                    "Gateway capture call for {PaymentId} returned an infrastructure error; saga must retry.",
                    tx.Id);
                return Result.Fail(PaymentsErrors.GatewayUnavailable());
            }

            var reason = GatewayResponseClassifier.Classify(declined.GatewayCode);
            var failureInfo = FailureInfo.Create(reason, declined.GatewayCode, utcNow);
            var failedResult = tx.MarkCaptureFailed(failureInfo, utcNow);
            if (failedResult.IsFailed)
            {
                return failedResult;
            }
        }

        foreach (var domainEvent in tx.PopDomainEvents())
        {
            await _dispatcher.DispatchAsync(domainEvent, ct);
        }

        await _outbox.SaveChangesAsync(ct);

        return Result.Ok();
    }
}
