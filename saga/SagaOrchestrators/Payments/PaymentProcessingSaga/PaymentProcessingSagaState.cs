using Platform.SharedKernel.Base;
using SagaOrchestrators.Common.SagaAbstractions;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga;

/// <summary>
/// Represents the state of the <see cref="PaymentProcessingSagaOrchestrator"/>. The eShop
/// always creates an Order before initiating payment, so the saga is keyed on the order's id
/// (ADR-0029): <see cref="CorrelationId"/> == OrderId, one payment process per order.
/// Lifecycle (ADR-0026 capture pivot): authorize -&gt; await capture approval -&gt; capture,
/// with a pre-capture void on the compensation path.
/// </summary>
public sealed class PaymentProcessingSagaState : ISagaStateInstance, IAuditableEntity
{
    /// <summary>
    /// Uniquely identifies the saga instance. Equals the pre-assigned OrderId (ADR-0029) —
    /// one payment process per order. Forwarded to the Payments BC as the OrderId on every
    /// outbound command.
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// Current state of the saga state machine.
    /// </summary>
    public string CurrentState { get; set; } = ""; // always auto set by factory

    /// <summary>
    /// Identifier of the user making the payment.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gateway-issued opaque payment-method token (Stripe 'pm_*', Adyen alphanumeric, …);
    /// 1-64 chars. Held as a string so the saga can carry real PSP tokens once a live
    /// gateway adapter ships.
    /// </summary>
    public string PaymentMethodId { get; set; } = string.Empty;

    /// <summary>
    /// Payment amount.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// ISO 4217 currency code (e.g., 'USD', 'EUR').
    /// </summary>
    public string Currency { get; set; }

    /// <summary>
    /// Idempotency key for preventing duplicate payment processing.
    /// </summary>
    public string IdempotencyKey { get; set; }

    /// <summary>
    /// Authorization ID from the payment provider (set after successful authorization).
    /// </summary>
    public string? AuthorizationId { get; set; }

    /// <summary>
    /// UTC timestamp when the authorization expires.
    /// </summary>
    public DateTimeOffset? AuthorizationExpiresAtUtc { get; set; }

    /// <summary>
    /// Payment transaction ID after successful capture (used for refunds).
    /// </summary>
    public Guid? PaymentTransactionId { get; set; }

    /// <summary>
    /// UTC timestamp when the payment was initiated.
    /// </summary>
    public DateTimeOffset InitiatedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when the saga was created.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; }

    /// <summary>
    /// UTC timestamp when the saga was last modified.
    /// </summary>
    public DateTimeOffset LastModifiedUtc { get; }

    /// <summary>
    /// UTC timestamp when authorization was completed (null if not yet authorized).
    /// </summary>
    public DateTimeOffset? AuthorizedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when capture was completed (null if not yet captured).
    /// </summary>
    public DateTimeOffset? CapturedAtUtc { get; set; }

    /// <summary>
    /// Number of authorization retry attempts.
    /// </summary>
    public int AuthorizationRetryCount { get; set; }

    /// <summary>
    /// Number of capture retry attempts.
    /// </summary>
    public int CaptureRetryCount { get; set; }

    /// <summary>
    /// Error code if the payment failed.
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Error message if the payment failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Indicates if compensation (a pre-capture void) has been triggered.
    /// </summary>
    public bool CompensationTriggered { get; set; }

    /// <summary>
    /// UTC timestamp when compensation was completed (null if not applicable).
    /// </summary>
    public DateTimeOffset? CompensationCompletedAtUtc { get; set; }

    /// <summary>
    /// Optimistic concurrency token.
    /// </summary>
    public uint RowVersion { get; set; }

    // Scheduler tokens for MassTransit scheduled messages
    public Guid? AuthorizationTimeoutTokenId { get; set; }
    public Guid? CaptureApprovalTimeoutTokenId { get; set; }
    public Guid? CaptureTimeoutTokenId { get; set; }
    public Guid? VoidTimeoutTokenId { get; set; }

    /// <summary>
    /// Terminal states that indicate the saga has completed (successfully or with failure).
    /// Sagas in these states should not be considered "stuck". Each transitions straight to
    /// <c>Finalize()</c> so a healthy saga is removed from the table on reaching one; this list
    /// backstops the stuck-saga health check against any instance that lingers. Per ADR-0026
    /// <c>PaymentCompleted</c> is now terminal (refund is a deferred customer/admin flow, not a
    /// post-completion wait-state) and the refund states were removed.
    /// </summary>
    public static readonly string[] TerminalStates =
    [
        nameof(PaymentProcessingSagaOrchestrator.AuthorizationFailed),
        nameof(PaymentProcessingSagaOrchestrator.PaymentCompleted),
        nameof(PaymentProcessingSagaOrchestrator.VoidCompleted),
        nameof(PaymentProcessingSagaOrchestrator.VoidFailed)
    ];
}
