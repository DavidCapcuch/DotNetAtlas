using DotNetAtlas.SharedKernel.Base;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentSaga;

/// <summary>
/// Represents the state of a payment processing saga.
/// This is a "dumb" payment saga - it knows nothing about business context (subscriptions, orders, etc.).
/// It only handles the payment lifecycle: authorization -> capture -> void/refund.
/// </summary>
public sealed class PaymentSagaState : SagaStateMachineInstance, ISagaVersion, ISagaAuditableEntity
{
    /// <summary>
    /// Uniquely identifies the saga instance.
    /// Correlates all events in the payment flow.
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// Current state of the saga state machine.
    /// </summary>
    public string CurrentState { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the user making the payment.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// ID of the saved payment method used for this transaction.
    /// </summary>
    public Guid PaymentMethodId { get; set; }

    /// <summary>
    /// Payment amount.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// ISO 4217 currency code (e.g., 'USD', 'EUR').
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Idempotency key for preventing duplicate payment processing.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

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
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when the saga was last updated.
    /// </summary>
    public DateTimeOffset LastUpdatedAtUtc { get; set; }

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
    /// Indicates if compensation (void or refund) has been triggered.
    /// </summary>
    public bool CompensationTriggered { get; set; }

    /// <summary>
    /// UTC timestamp when compensation was completed (null if not applicable).
    /// </summary>
    public DateTimeOffset? CompensationCompletedAtUtc { get; set; }

    /// <summary>
    /// Version number for optimistic concurrency control.
    /// </summary>
    public int Version { get; set; }

    // Scheduler tokens for MassTransit scheduled messages
    public Guid? AuthorizationTimeoutTokenId { get; set; }
    public Guid? CaptureTimeoutTokenId { get; set; }
    public Guid? VoidTimeoutTokenId { get; set; }
    public Guid? RefundTimeoutTokenId { get; set; }
}
