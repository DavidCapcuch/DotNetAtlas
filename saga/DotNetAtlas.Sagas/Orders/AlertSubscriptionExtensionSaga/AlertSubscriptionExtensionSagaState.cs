using DotNetAtlas.Sagas.Common.SagaAbstractions;
using DotNetAtlas.SharedKernel.Base;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga;

/// <summary>
/// Represents the state of the <see cref="AlertSubscriptionExtensionSaga"/>.
/// This saga orchestrates the alert subscription extension flow, coordinating payment
/// (via <c>PaymentRequestedEvent</c>) and extension (via <c>ExtendAlertSubscriptionCommand</c>).
/// </summary>
public sealed class AlertSubscriptionExtensionSagaState : ISagaStateInstance, IAuditableEntity
{
    /// <summary>
    /// Uniquely identifies the saga instance.
    /// Correlates all events in the subscription extension flow (extension → payment → extend).
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// Current state of the saga state machine.
    /// </summary>
    public string CurrentState { get; set; } = ""; // always auto set by factory

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
    public string Currency { get; set; }

    /// <summary>
    /// Idempotency key for preventing duplicate extensions.
    /// </summary>
    public string IdempotencyKey { get; set; }

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
    public DateTimeOffset CreatedUtc { get; }

    /// <summary>
    /// UTC timestamp when the saga was last modified.
    /// </summary>
    public DateTimeOffset LastModifiedUtc { get; }

    /// <summary>
    /// UTC timestamp when extension was completed, if successful.
    /// </summary>
    public DateTimeOffset? ExtensionCompletedAtUtc { get; set; }

    /// <summary>
    /// New expiration date after extension, if successful.
    /// </summary>
    public DateTimeOffset? NewExpiresAtUtc { get; set; }

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
    /// Optimistic concurrency token.
    /// </summary>
    public byte[]? RowVersion { get; set; }

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

    /// <summary>
    /// Terminal states that indicate the saga has completed (successfully or with failure).
    /// Sagas in these states should not be considered "stuck".
    /// </summary>
    public static readonly string[] TerminalStates =
    [
        nameof(AlertSubscriptionExtensionSaga.ExtensionCompleted),
        nameof(AlertSubscriptionExtensionSaga.ExtensionFailed),
        nameof(AlertSubscriptionExtensionSaga.CompensationCompleted),
        nameof(AlertSubscriptionExtensionSaga.CompensationFailed),
        nameof(AlertSubscriptionExtensionSaga.PaymentFailed)
    ];
}
