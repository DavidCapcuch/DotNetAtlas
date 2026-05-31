using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Payments.Application.Abstractions;
using Payments.Application.Common.Data;
using Payments.Domain.Errors;
using Payments.Domain.Transactions;
using Payments.Domain.Transactions.ValueObjects;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.ValueObjects;

namespace Payments.Application.Transactions.AuthorizePayment;

/// <summary>
/// Handles <see cref="AuthorizePaymentCommand"/> — the saga's entry point into the Payments
/// BC. Loads (or creates) the aggregate, persists it in <c>Requested</c>, calls the gateway,
/// transitions the aggregate to <c>Authorized</c>/<c>Failed</c>, then flushes outbox rows in a
/// second SaveChanges (H-3 closeout).
/// </summary>
/// <remarks>
/// <para>
/// <b>H-3 — double-charge protection.</b> A naive single-SaveChanges flow has a window where
/// the gateway succeeds but SaveChanges fails (DB blip, deadlock, concurrency violation); the
/// inbox-dedup row is rolled back, saga retries re-enter the handler via the
/// <c>existing is null</c> branch, and the gateway gets called again. Splitting into two
/// SaveChanges sites — first the aggregate-created Requested state (durable inbox-dedup
/// anchor), then the post-gateway transition — closes that window. Combined with
/// <c>IPaymentGateway.AuthorizeAsync(tx, idempotencyKey, ct)</c> (H-4) the gateway-side has
/// an independent dedup safety net for the rare cases where the first-save fails mid-flight.
/// </para>
/// <para>
/// Failure semantics:
/// <list type="bullet">
///   <item><description>Gateway <b>decline</b> (business-expected, e.g. <c>insufficient_funds</c>):
///     aggregate transitions to <c>Failed</c> and the handler returns <c>Result.Ok()</c>. The
///     saga learns of the decline via the emitted <c>PaymentAuthorizationFailedEvent</c> on
///     the outbox.</description></item>
///   <item><description>Gateway <b>infrastructure error</b> (timeout, unreachable): handler
///     returns <c>Result.Fail(PaymentsErrors.GatewayUnavailable())</c>. The saga / outbox
///     relay retries per ADR-0010.</description></item>
///   <item><description>Aggregate already past <c>Requested</c>: idempotent no-op. Saga retries
///     are deduplicated by the inbox; this short-circuit is defense-in-depth.</description></item>
/// </list>
/// </para>
/// </remarks>
internal sealed class AuthorizePaymentCommandHandler : ICommandHandler<AuthorizePaymentCommand, Guid>
{
    private readonly IPaymentsDbContext _dbContext;
    private readonly IPaymentGateway _gateway;
    private readonly ITransactionalOutbox<IPaymentsDbContext> _outbox;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuthorizePaymentCommandHandler> _logger;

    public AuthorizePaymentCommandHandler(
        IPaymentsDbContext dbContext,
        IPaymentGateway gateway,
        ITransactionalOutbox<IPaymentsDbContext> outbox,
        TimeProvider timeProvider,
        ILogger<AuthorizePaymentCommandHandler> logger)
    {
        _dbContext = dbContext;
        _gateway = gateway;
        _outbox = outbox;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<Guid>> HandleAsync(AuthorizePaymentCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        // PK lookup (ADR-0022): inline LINQ, tracked for mutation. No spec — the predicate has
        // no business name and PaymentTransaction has no child collections to include.
        var existing = await _dbContext.Transactions
            .FirstOrDefaultAsync(t => t.Id == command.PaymentId, ct);

        PaymentTransaction tx;
        if (existing is null)
        {
            var moneyResult = Money.Create(command.Amount, command.Currency);
            if (moneyResult.IsFailed)
            {
                return moneyResult.ToResult<Guid>();
            }

            var createResult = PaymentTransaction.Create(
                command.PaymentId,
                command.CorrelationId,
                command.BuyerId,
                command.OrderId,
                moneyResult.Value,
                command.PaymentMethodId);
            if (createResult.IsFailed)
            {
                return createResult.ToResult<Guid>();
            }

            tx = createResult.Value;
            _dbContext.Transactions.Add(tx);

            // H-3: persist the Requested aggregate + inbox-dedup row BEFORE the gateway call.
            // If SaveChanges below fails, saga retry re-enters via the `existing is null` branch
            // and re-creates — but the gateway has not been touched yet, so no double-authorize
            // is possible. PaymentTransaction.Create raises no domain events (ADR-0023 follow-up),
            // so the DispatchDomainEventsInterceptor wired on PaymentsDbContext is a no-op here.
            await _outbox.SaveChangesAsync(ct);
        }
        else
        {
            tx = existing;
            if (tx.Status != PaymentStatus.Requested)
            {
                _logger.LogInformation(
                    "AuthorizePaymentCommand received for {PaymentId} already in '{Status}'; idempotent no-op.",
                    tx.Id,
                    tx.Status.Name);
                return Result.Ok(tx.Id);
            }
        }

        var gatewayResult = await _gateway.AuthorizeAsync(tx, command.IdempotencyKey, ct);
        var utcNow = _timeProvider.GetUtcNow();

        if (gatewayResult.IsSuccess)
        {
            var authorizeResult = tx.Authorize(
                gatewayResult.Value.GatewayTransactionId,
                gatewayResult.Value.ResponseCode,
                gatewayResult.Value.ExpiresAtUtc,
                utcNow);
            if (authorizeResult.IsFailed)
            {
                return authorizeResult.ToResult<Guid>();
            }
        }
        else
        {
            var declined = gatewayResult.Errors.OfType<GatewayDeclinedError>().FirstOrDefault();
            if (declined is null)
            {
                _logger.LogWarning(
                    "Gateway authorize call for {PaymentId} returned an infrastructure error; saga must retry.",
                    tx.Id);
                return Result.Fail<Guid>(PaymentsErrors.GatewayUnavailable());
            }

            var reason = GatewayResponseClassifier.Classify(declined.GatewayCode);
            var failureInfo = FailureInfo.Create(reason, declined.GatewayCode, utcNow);
            var failedResult = tx.MarkAuthorizationFailed(failureInfo, utcNow);
            if (failedResult.IsFailed)
            {
                return failedResult.ToResult<Guid>();
            }
        }

        // Dispatch of PaymentAuthorizedDomainEvent / PaymentAuthorizationFailedDomainEvent /
        // PaymentFailedDomainEvent is the DispatchDomainEventsInterceptor's job — it fires inside
        // SavingChangesAsync, walks ChangeTracker.Entries<IAggregateRoot>(), pops their events,
        // and dispatches them in the same DI scope so outbox publishers' AddOutboxMessage calls
        // land in the same transaction as the aggregate save (reliable-messaging guarantee).
        // Handlers must NOT inject IDomainEventDispatcher directly — dispatch is an infrastructure
        // concern, owned by the interceptor. See services/Payments/Payments.Infrastructure/
        // Persistence/Database/Interceptors/DispatchDomainEventsInterceptor.cs.
        await _outbox.SaveChangesAsync(ct);

        return Result.Ok(tx.Id);
    }
}
