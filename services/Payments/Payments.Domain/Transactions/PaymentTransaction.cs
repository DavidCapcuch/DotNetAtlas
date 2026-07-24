using FluentResults;
using Payments.Domain.Errors;
using Payments.Domain.Transactions.Events;
using Payments.Domain.Transactions.ValueObjects;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Payments.Domain.Transactions;

/// <summary>
/// Aggregate root representing a single saga-scoped payment lifecycle. Owns the payment state
/// machine, the gateway-transaction-id token (append-only per I-4), and the terminal-failure
/// metadata. Once the aggregate reaches a final status (<see cref="PaymentStatus.Failed"/>,
/// <see cref="PaymentStatus.Voided"/>, <see cref="PaymentStatus.Refunded"/>) no further mutations
/// are accepted; saga retries become idempotent no-ops.
/// </summary>
/// <remarks>
/// This aggregate raises (at most) the following domain events per lifecycle. Note that
/// <see cref="Create"/> deliberately does NOT raise a domain event — the Payments BC is invoked
/// through <c>AuthorizePaymentCommand</c> from PaymentProcessingSaga, and the wire-level
/// <c>RequestPaymentCommand</c> (per ADR-0023) is produced by the Checkout saga, not by Payments.
/// A Payments-internal "requested" event would have no in-process handler and no outbox
/// publisher, so none is raised.
/// <list type="bullet">
/// <item><see cref="PaymentAuthorizedDomainEvent"/>: on successful <see cref="Authorize"/>.</item>
/// <item><see cref="PaymentAuthorizationFailedDomainEvent"/> + <see cref="PaymentFailedDomainEvent"/>:
///   on <see cref="MarkAuthorizationFailed"/>.</item>
/// <item><see cref="PaymentCapturedDomainEvent"/> + <see cref="PaymentCompletedDomainEvent"/>:
///   on successful <see cref="Capture"/> (v1 auto-completion per
///   <c>docs/bc-design/payments.md § 4</c>).</item>
/// <item><see cref="PaymentCaptureFailedDomainEvent"/> + <see cref="PaymentFailedDomainEvent"/>:
///   on <see cref="MarkCaptureFailed"/>.</item>
/// <item><see cref="PaymentVoidedDomainEvent"/>: on <see cref="Void"/>.</item>
/// <item><see cref="PaymentRefundedDomainEvent"/>: on <see cref="Refund"/>.</item>
/// </list>
/// </remarks>
public sealed class PaymentTransaction : AggregateRoot<Guid>
{
    /// <summary>
    /// Buyer identifier (JWT <c>sub</c> at checkout). Immutable per I-6.
    /// </summary>
    public Guid BuyerId { get; private set; }

    /// <summary>
    /// Order aggregate this payment is attached to. Immutable per I-6.
    /// </summary>
    public Guid OrderId { get; private set; }

    /// <summary>
    /// Total amount to charge. Positive per I-1; single currency per I-2.
    /// </summary>
    public Money Amount { get; private set; } = null!;

    /// <summary>
    /// Gateway-issued tokenised payment instrument (never a raw PAN/CVV). Immutable post-creation.
    /// </summary>
    public PaymentMethodId PaymentMethodId { get; private set; } = null!;

    /// <summary>
    /// Current lifecycle status. Transitions are guarded by <see cref="PaymentStatus.CanTransitionTo"/>.
    /// </summary>
    public PaymentStatus Status { get; private set; } = null!;

    /// <summary>
    /// Gateway-side transaction reference. Null until the first successful gateway call; append-only per I-4.
    /// </summary>
    public string? GatewayTransactionId { get; private set; }

    /// <summary>
    /// Last observed gateway response code (success or failure). Updated on each gateway call.
    /// </summary>
    public GatewayResponseCode? GatewayResponseCode { get; private set; }

    public DateTimeOffset? AuthorizedAtUtc { get; private set; }

    public DateTimeOffset? CapturedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public DateTimeOffset? RefundedAtUtc { get; private set; }

    public DateTimeOffset? VoidedAtUtc { get; private set; }

    /// <summary>
    /// Saga-supplied reason for the void. Null until <see cref="Void"/> succeeds.
    /// </summary>
    public string? VoidReason { get; private set; }

    /// <summary>
    /// Terminal-failure details. Populated when <see cref="Status"/> becomes <see cref="PaymentStatus.Failed"/>.
    /// </summary>
    public FailureInfo? FailureInfo { get; private set; }

    private PaymentTransaction()
    {
    }

    /// <summary>
    /// Creates a new <see cref="PaymentTransaction"/> in <see cref="PaymentStatus.Requested"/>.
    /// </summary>
    /// <param name="paymentId">Aggregate identity (UUID v7 recommended by caller).</param>
    /// <param name="buyerId">Buyer JWT <c>sub</c>.</param>
    /// <param name="orderId">Associated Ordering aggregate id.</param>
    /// <param name="amount">Amount to charge; must be positive. Positivity (I-1) is enforced by this
    /// factory — <see cref="Money"/> is permissive on sign (School B); single currency (I-2) is
    /// structural to <see cref="Money"/>'s currency tag.</param>
    /// <param name="paymentMethodId">Tokenised payment instrument string (1-64 chars).</param>
    /// <returns>Ok with the new aggregate, or failure with <see cref="PaymentsErrors.InvalidAmount"/>
    /// / <see cref="PaymentsErrors.InvalidPaymentMethod"/>.</returns>
    /// <remarks>
    /// Per ADR-0023, raises no domain events. The aggregate's creation moment is signalled
    /// to the saga via <c>RequestPaymentCommand</c> (Checkout-saga produced, on
    /// <c>payments.payment-commands</c>), not via a Payments-internal domain event. No
    /// timestamp parameter is needed — the mutator methods (<see cref="Authorize"/>,
    /// <see cref="Capture"/>, etc.) carry their own <c>utcNow</c> for the events they raise.
    /// </remarks>
    public static Result<PaymentTransaction> Create(
        Guid paymentId,
        Guid buyerId,
        Guid orderId,
        Money amount,
        string paymentMethodId)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (amount.Amount <= 0)
        {
            return Result.Fail<PaymentTransaction>(PaymentsErrors.InvalidAmount());
        }

        var paymentMethodIdResult = PaymentMethodId.Create(paymentMethodId);
        if (paymentMethodIdResult.IsFailed)
        {
            return Result.Fail<PaymentTransaction>(paymentMethodIdResult.Errors);
        }

        var paymentTransaction = new PaymentTransaction
        {
            Id = paymentId,
            BuyerId = buyerId,
            OrderId = orderId,
            Amount = amount,
            PaymentMethodId = paymentMethodIdResult.Value,
            Status = PaymentStatus.Requested,
        };

        return Result.Ok(paymentTransaction);
    }

    /// <summary>
    /// Transitions the aggregate from <see cref="PaymentStatus.Requested"/> to
    /// <see cref="PaymentStatus.Authorized"/> after a successful gateway authorize call.
    /// Idempotent no-op when the aggregate is already <see cref="PaymentStatus.Authorized"/>
    /// with the same <paramref name="gatewayTransactionId"/>.
    /// </summary>
    /// <param name="gatewayTransactionId">Gateway-issued transaction id (non-empty).</param>
    /// <param name="gatewayResponseCode">Gateway response.</param>
    /// <param name="expiresAtUtc">Gateway-stated authorization expiry — sourced from the
    /// <c>AuthorizeResponse</c> and flowed onto <see cref="PaymentAuthorizedDomainEvent.ExpiresAtUtc"/>
    /// so the outbound Avro event is truthful.</param>
    /// <param name="utcNow">Current UTC time.</param>
    /// <exception cref="DataIntegrityException">Invalid FSM transition (I-3/I-5) or mismatched
    /// <c>GatewayTransactionId</c> (I-4).</exception>
    public Result Authorize(
        string gatewayTransactionId,
        GatewayResponseCode gatewayResponseCode,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayTransactionId);
        ArgumentNullException.ThrowIfNull(gatewayResponseCode);

        GuardAppendOnlyGatewayTransactionId(gatewayTransactionId);

        if (Status == PaymentStatus.Authorized)
        {
            return Result.Ok();
        }

        GuardTransition(PaymentStatus.Authorized);

        GatewayTransactionId = gatewayTransactionId;
        GatewayResponseCode = gatewayResponseCode;
        AuthorizedAtUtc = utcNow;
        Status = PaymentStatus.Authorized;

        AddDomainEvent(new PaymentAuthorizedDomainEvent
        {
            PaymentId = Id,
            BuyerId = BuyerId,
            OrderId = OrderId,
            GatewayTransactionId = gatewayTransactionId,
            Amount = Amount,
            AuthorizedAtUtc = utcNow,
            ExpiresAtUtc = expiresAtUtc,
            OccurredOnUtc = utcNow,
        });

        return Result.Ok();
    }

    /// <summary>
    /// Transitions the aggregate from <see cref="PaymentStatus.Requested"/> to
    /// <see cref="PaymentStatus.Failed"/> after a business-expected gateway decline on authorize.
    /// Raises <see cref="PaymentAuthorizationFailedDomainEvent"/> followed by
    /// <see cref="PaymentFailedDomainEvent"/>. Idempotent when already <see cref="PaymentStatus.Failed"/>.
    /// </summary>
    /// <param name="failureInfo">Populated failure metadata.</param>
    /// <param name="utcNow">Current UTC time.</param>
    /// <exception cref="DataIntegrityException">Invalid FSM transition.</exception>
    public Result MarkAuthorizationFailed(FailureInfo failureInfo, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(failureInfo);

        if (Status == PaymentStatus.Failed)
        {
            return Result.Ok();
        }

        // Source-state guard: authorize-failure can only come from Requested. The FSM table also
        // permits Authorized → Failed (for MarkCaptureFailed), so the target-guard alone is not
        // enough to reject wrong-phase calls.
        Throw.If(Status != PaymentStatus.Requested, new DataIntegrityException(
            "Payments.MarkAuthorizationFailed.InvalidSourceStatus",
            $"MarkAuthorizationFailed is only valid from '{PaymentStatus.Requested.Name}'; current: '{Status.Name}'."));

        GuardTransition(PaymentStatus.Failed);

        FailureInfo = failureInfo;
        Status = PaymentStatus.Failed;

        AddDomainEvent(new PaymentAuthorizationFailedDomainEvent
        {
            PaymentId = Id,
            BuyerId = BuyerId,
            OrderId = OrderId,
            FailureInfo = failureInfo,
            OccurredOnUtc = utcNow,
        });

        AddDomainEvent(new PaymentFailedDomainEvent
        {
            PaymentId = Id,
            BuyerId = BuyerId,
            OrderId = OrderId,
            FailureInfo = failureInfo,
            FailedAtUtc = utcNow,
            OccurredOnUtc = utcNow,
        });

        return Result.Ok();
    }

    /// <summary>
    /// Transitions the aggregate from <see cref="PaymentStatus.Authorized"/> through
    /// <see cref="PaymentStatus.Captured"/> to <see cref="PaymentStatus.Completed"/> (auto-advance
    /// per v1; see <c>docs/bc-design/payments.md § 4</c>). Raises <see cref="PaymentCapturedDomainEvent"/>
    /// then <see cref="PaymentCompletedDomainEvent"/>. Idempotent when already in
    /// <see cref="PaymentStatus.Captured"/> or <see cref="PaymentStatus.Completed"/>.
    /// </summary>
    /// <param name="gatewayTransactionId">Gateway transaction id (must match stored value per I-4).</param>
    /// <param name="gatewayResponseCode">Gateway response.</param>
    /// <param name="utcNow">Current UTC time.</param>
    /// <exception cref="DataIntegrityException">Invalid FSM transition or <c>GatewayTransactionId</c>
    /// mismatch (I-4).</exception>
    public Result Capture(string gatewayTransactionId, GatewayResponseCode gatewayResponseCode, DateTimeOffset utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayTransactionId);
        ArgumentNullException.ThrowIfNull(gatewayResponseCode);

        GuardAppendOnlyGatewayTransactionId(gatewayTransactionId);

        if (Status == PaymentStatus.Captured || Status == PaymentStatus.Completed)
        {
            return Result.Ok();
        }

        GuardTransition(PaymentStatus.Captured);

        GatewayTransactionId = gatewayTransactionId;
        GatewayResponseCode = gatewayResponseCode;
        CapturedAtUtc = utcNow;
        Status = PaymentStatus.Captured;

        AddDomainEvent(new PaymentCapturedDomainEvent
        {
            PaymentId = Id,
            BuyerId = BuyerId,
            OrderId = OrderId,
            GatewayTransactionId = gatewayTransactionId,
            Amount = Amount,
            CapturedAtUtc = utcNow,
            OccurredOnUtc = utcNow,
        });

        // v1 auto-completion per payments.md § 4 table: "Captured | (auto) | Completed".
        CompletedAtUtc = utcNow;
        Status = PaymentStatus.Completed;

        AddDomainEvent(new PaymentCompletedDomainEvent
        {
            PaymentId = Id,
            BuyerId = BuyerId,
            OrderId = OrderId,
            Amount = Amount,
            CompletedAtUtc = utcNow,
            OccurredOnUtc = utcNow,
        });

        return Result.Ok();
    }

    /// <summary>
    /// Transitions the aggregate from <see cref="PaymentStatus.Authorized"/> to
    /// <see cref="PaymentStatus.Failed"/> after a capture-side gateway failure.
    /// Raises <see cref="PaymentCaptureFailedDomainEvent"/> followed by
    /// <see cref="PaymentFailedDomainEvent"/>. Idempotent when already <see cref="PaymentStatus.Failed"/>.
    /// </summary>
    /// <param name="failureInfo">Populated failure metadata.</param>
    /// <param name="utcNow">Current UTC time.</param>
    /// <exception cref="DataIntegrityException">Invalid FSM transition.</exception>
    public Result MarkCaptureFailed(FailureInfo failureInfo, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(failureInfo);

        if (Status == PaymentStatus.Failed)
        {
            return Result.Ok();
        }

        // Source-state guard: capture-failure can only come from Authorized. The FSM target-guard
        // alone would accept Requested → Failed (needed for MarkAuthorizationFailed) which is the
        // wrong phase for this method.
        Throw.If(Status != PaymentStatus.Authorized, new DataIntegrityException(
            "Payments.MarkCaptureFailed.InvalidSourceStatus",
            $"MarkCaptureFailed is only valid from '{PaymentStatus.Authorized.Name}'; current: '{Status.Name}'."));

        GuardTransition(PaymentStatus.Failed);

        // GatewayTransactionId was set by Authorize and is append-only (I-4); the source-state
        // guard above proves we passed through Authorized, so the bang is safe.
        var gatewayTransactionId = GatewayTransactionId!;

        FailureInfo = failureInfo;
        Status = PaymentStatus.Failed;

        AddDomainEvent(new PaymentCaptureFailedDomainEvent
        {
            PaymentId = Id,
            BuyerId = BuyerId,
            OrderId = OrderId,
            GatewayTransactionId = gatewayTransactionId,
            FailureInfo = failureInfo,
            OccurredOnUtc = utcNow,
        });

        AddDomainEvent(new PaymentFailedDomainEvent
        {
            PaymentId = Id,
            BuyerId = BuyerId,
            OrderId = OrderId,
            FailureInfo = failureInfo,
            FailedAtUtc = utcNow,
            OccurredOnUtc = utcNow,
        });

        return Result.Ok();
    }

    /// <summary>
    /// Transitions the aggregate from <see cref="PaymentStatus.Authorized"/> to
    /// <see cref="PaymentStatus.Voided"/> as saga pre-capture compensation. Raises
    /// <see cref="PaymentVoidedDomainEvent"/>. Idempotent when already <see cref="PaymentStatus.Voided"/>.
    /// </summary>
    /// <param name="reason">Saga-supplied reason for the void (audit trail; persisted on the
    /// aggregate and surfaced on <c>PaymentVoidedEvent.Reason</c>).</param>
    /// <param name="gatewayResponseCode">Gateway response from the void call.</param>
    /// <param name="utcNow">Current UTC time.</param>
    /// <exception cref="DataIntegrityException">Invalid FSM transition.</exception>
    public Result Void(string reason, GatewayResponseCode gatewayResponseCode, DateTimeOffset utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(gatewayResponseCode);

        if (Status == PaymentStatus.Voided)
        {
            return Result.Ok();
        }

        GuardTransition(PaymentStatus.Voided);

        Throw.If(GatewayTransactionId is null, new DataIntegrityException(
            "Payments.VoidWithoutGatewayTransactionId",
            "Cannot void a payment that has no gateway transaction id."));

        GatewayResponseCode = gatewayResponseCode;
        VoidedAtUtc = utcNow;
        VoidReason = reason;
        Status = PaymentStatus.Voided;

        AddDomainEvent(new PaymentVoidedDomainEvent
        {
            PaymentId = Id,
            BuyerId = BuyerId,
            OrderId = OrderId,
            GatewayTransactionId = GatewayTransactionId!,
            VoidedAtUtc = utcNow,
            Reason = reason,
            OccurredOnUtc = utcNow,
        });

        return Result.Ok();
    }

    /// <summary>
    /// Transitions the aggregate from <see cref="PaymentStatus.Captured"/> or
    /// <see cref="PaymentStatus.Completed"/> to <see cref="PaymentStatus.Refunded"/> as saga
    /// cancel-post-capture compensation. Raises <see cref="PaymentRefundedDomainEvent"/>.
    /// Idempotent when already <see cref="PaymentStatus.Refunded"/>.
    /// </summary>
    /// <param name="refundReason">Reason the saga issued the refund.</param>
    /// <param name="gatewayResponseCode">Gateway response from the refund call.</param>
    /// <param name="utcNow">Current UTC time.</param>
    /// <exception cref="DataIntegrityException">Invalid FSM transition.</exception>
    public Result Refund(string refundReason, GatewayResponseCode gatewayResponseCode, DateTimeOffset utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refundReason);
        ArgumentNullException.ThrowIfNull(gatewayResponseCode);

        if (Status == PaymentStatus.Refunded)
        {
            return Result.Ok();
        }

        GuardTransition(PaymentStatus.Refunded);

        Throw.If(GatewayTransactionId is null, new DataIntegrityException(
            "Payments.RefundWithoutGatewayTransactionId",
            "Cannot refund a payment that has no gateway transaction id."));

        GatewayResponseCode = gatewayResponseCode;
        RefundedAtUtc = utcNow;
        Status = PaymentStatus.Refunded;

        AddDomainEvent(new PaymentRefundedDomainEvent
        {
            PaymentId = Id,
            BuyerId = BuyerId,
            OrderId = OrderId,
            GatewayTransactionId = GatewayTransactionId!,
            Amount = Amount,
            Reason = refundReason,
            RefundedAtUtc = utcNow,
            OccurredOnUtc = utcNow,
        });

        return Result.Ok();
    }

    private void GuardTransition(PaymentStatus target)
    {
        Throw.If(!Status.CanTransitionTo(target), new DataIntegrityException(
            "Payments.InvalidStatusTransition",
            $"Invalid payment status transition from '{Status.Name}' to '{target.Name}'."));
    }

    private void GuardAppendOnlyGatewayTransactionId(string incoming)
    {
        Throw.If(
            GatewayTransactionId is not null && !string.Equals(GatewayTransactionId, incoming, StringComparison.Ordinal),
            new DataIntegrityException(
                "Payments.GatewayTransactionIdImmutable",
                $"GatewayTransactionId is append-only (I-4): stored '{GatewayTransactionId}', incoming '{incoming}'."));
    }
}
