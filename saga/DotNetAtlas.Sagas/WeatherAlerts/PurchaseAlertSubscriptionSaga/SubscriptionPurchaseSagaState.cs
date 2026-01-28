using DotNetAtlas.SharedKernel.Base;
using MassTransit;
using Order.AlertSubscriptions;

namespace DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga;

/// <summary>
/// Represents the state of a subscription purchase saga.
/// This saga is the "smart" business saga that orchestrates the purchase flow.
/// It coordinates payment (via PaymentRequestedEvent) and activation (via ActivateSubscriptionCommand).
/// </summary>
public sealed class SubscriptionPurchaseSagaState : SagaStateMachineInstance, ISagaVersion, ISagaAuditableEntity
{
    /// <summary>
    /// Uniquely identifies the saga instance.
    /// Correlates all events in the subscription purchase flow (purchase → payment → activation).
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// Current state of the saga state machine.
    /// </summary>
    public string CurrentState { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the user who is purchasing the subscription.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// ID of the saved payment method to use for this purchase.
    /// </summary>
    public Guid PaymentMethodId { get; set; }

    /// <summary>
    /// The subscription tier being purchased.
    /// </summary>
    public SubscriptionTier SubscriptionTier { get; set; }

    /// <summary>
    /// Duration of the subscription in days.
    /// </summary>
    public int DurationDays { get; set; }

    /// <summary>
    /// Payment amount for the subscription.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// ISO 4217 currency code (e.g., 'USD', 'EUR').
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Idempotency key for preventing duplicate purchases.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// Payment transaction ID (set after payment is completed).
    /// Used for compensation (refunds) if activation fails.
    /// </summary>
    public Guid? PaymentTransactionId { get; set; }

    /// <summary>
    /// UTC timestamp when the purchase was initiated.
    /// </summary>
    public DateTimeOffset PurchaseInitiatedAtUtc { get; set; }

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
    /// UTC timestamp when activation was completed, if successful.
    /// </summary>
    public DateTimeOffset? ActivationCompletedAtUtc { get; set; }

    /// <summary>
    /// Number of retry attempts for activation.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Error message if activation failed.
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
    /// Token ID for the activation timeout scheduler.
    /// Used by MassTransit to manage scheduled messages.
    /// </summary>
    public Guid? ActivationTimeoutTokenId { get; set; }

    /// <summary>
    /// Token ID for the compensation timeout scheduler.
    /// Used by MassTransit to manage compensation timeout messages.
    /// </summary>
    public Guid? CompensationTimeoutTokenId { get; set; }
}
