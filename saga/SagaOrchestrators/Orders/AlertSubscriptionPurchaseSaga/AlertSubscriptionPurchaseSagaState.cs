using Order.AlertSubscriptions;
using Platform.SharedKernel.Base;
using SagaOrchestrators.Common.SagaAbstractions;

namespace SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga;

/// <summary>
/// Represents the state of the <see cref="AlertSubscriptionPurchaseSagaOrchestrator"/>.
/// This saga orchestrates the alert subscription purchase flow, coordinating payment
/// (via <c>PaymentRequestedEvent</c>) and activation (via <c>ActivateAlertSubscriptionCommand</c>).
/// </summary>
public sealed class AlertSubscriptionPurchaseSagaState : ISagaStateInstance, IAuditableEntity
{
    /// <summary>
    /// Uniquely identifies the saga instance.
    /// Correlates all events in the subscription purchase flow (purchase → payment → activation).
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// Current state of the saga state machine.
    /// </summary>
    public string CurrentState { get; set; } = ""; // always auto set by factory

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
    public string Currency { get; set; }

    /// <summary>
    /// Payment transaction ID (set after payment is completed).
    /// Used for compensation (refunds) if activation fails.
    /// </summary>
    public Guid? PaymentTransactionId { get; set; }

    /// <summary>
    /// UTC timestamp when the purchase was initiated.
    /// </summary>
    public DateTimeOffset PurchaseInitiatedUtc { get; set; }

    /// <summary>
    /// UTC timestamp when payment was completed, if successful.
    /// </summary>
    public DateTimeOffset? PaymentCompletedUtc { get; set; }

    /// <summary>
    /// UTC timestamp when the saga was created.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; }

    /// <summary>
    /// UTC timestamp when the saga was last modified.
    /// </summary>
    public DateTimeOffset LastModifiedUtc { get; }

    /// <summary>
    /// UTC timestamp when activation was completed, if successful.
    /// </summary>
    public DateTimeOffset? ActivationCompletedUtc { get; set; }

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
    public DateTimeOffset? CompensationCompletedUtc { get; set; }

    /// <summary>
    /// Optimistic concurrency token.
    /// </summary>
    public byte[]? RowVersion { get; set; }

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

    /// <summary>
    /// Terminal states that indicate the saga has completed (successfully or with failure).
    /// Sagas in these states should not be considered "stuck".
    /// </summary>
    public static readonly string[] TerminalStates =
    [
        nameof(AlertSubscriptionPurchaseSagaOrchestrator.ActivationCompleted),
        nameof(AlertSubscriptionPurchaseSagaOrchestrator.ActivationFailed),
        nameof(AlertSubscriptionPurchaseSagaOrchestrator.CompensationCompleted),
        nameof(AlertSubscriptionPurchaseSagaOrchestrator.CompensationFailed),
        nameof(AlertSubscriptionPurchaseSagaOrchestrator.PaymentFailed)
    ];
}
