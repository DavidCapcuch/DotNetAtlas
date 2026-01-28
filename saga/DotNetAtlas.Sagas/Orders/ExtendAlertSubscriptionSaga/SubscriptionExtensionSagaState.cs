using DotNetAtlas.SharedKernel.Base;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga;

/// <summary>
/// Represents the state of a subscription extension saga.
/// This saga is the "smart" business saga that orchestrates the extension flow.
/// It coordinates payment (via PaymentRequestedEvent) and extension (via ExtendSubscriptionCommand).
/// </summary>
public sealed class SubscriptionExtensionSagaState : SagaStateMachineInstance, ISagaVersion, ISagaAuditableEntity
{
    /// <summary>
    /// Uniquely identifies the saga instance.
    /// Correlates all events in the subscription extension flow (extension → payment → extend).
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// Current state of the saga state machine.
    /// </summary>
    public string CurrentState { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the user who is extending the subscription.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// ID of the saved payment method to use for this extension.
    /// </summary>
    public Guid PaymentMethodId { get; set; }

    /// <summary>
    /// Duration to extend the subscription in days.
    /// </summary>
    public int DurationDays { get; set; }

    /// <summary>
    /// Payment amount for the extension.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// ISO 4217 currency code (e.g., 'USD', 'EUR').
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Idempotency key for preventing duplicate extensions.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// Payment transaction ID (set after payment is completed).
    /// Used for compensation (refunds) if extension fails.
    /// </summary>
    public Guid? PaymentTransactionId { get; set; }

    /// <summary>
    /// UTC timestamp when the extension was initiated.
    /// </summary>
    public DateTimeOffset ExtensionInitiatedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when payment was completed, if successful.
    /// </summary>
    public DateTimeOffset? PaymentCompletedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when the saga was created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when the saga was last updated.
    /// </summary>
    public DateTimeOffset LastUpdatedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when extension was completed, if successful.
    /// </summary>
    public DateTimeOffset? ExtensionCompletedAtUtc { get; set; }

    /// <summary>
    /// New expiration date after extension, if successful.
    /// </summary>
    public DateTimeOffset? NewExpiresAtUtc { get; set; }

    /// <summary>
    /// Number of retry attempts for extension.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Error message if extension failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Error code for categorized failure handling.
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Indicates if compensation (refund) has been triggered.
    /// </summary>
    public bool CompensationTriggered { get; set; }

    /// <summary>
    /// UTC timestamp when compensation was completed, if applicable.
    /// </summary>
    public DateTimeOffset? CompensationCompletedAtUtc { get; set; }

    /// <summary>
    /// Version number for optimistic concurrency control.
    /// Automatically managed by MassTransit.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Token ID for the payment timeout scheduler.
    /// Used by MassTransit to manage scheduled messages.
    /// </summary>
    public Guid? PaymentTimeoutTokenId { get; set; }

    /// <summary>
    /// Token ID for the extension timeout scheduler.
    /// Used by MassTransit to manage scheduled messages.
    /// </summary>
    public Guid? ExtensionTimeoutTokenId { get; set; }

    /// <summary>
    /// Token ID for the compensation timeout scheduler.
    /// Used by MassTransit to manage compensation timeout messages.
    /// </summary>
    public Guid? CompensationTimeoutTokenId { get; set; }
}
