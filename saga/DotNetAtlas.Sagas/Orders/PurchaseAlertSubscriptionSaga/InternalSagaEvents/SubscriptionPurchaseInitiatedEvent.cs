using Order.AlertSubscriptions;

namespace DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event that initiates the subscription purchase saga.
/// Contains business context for the subscription purchase.
/// </summary>
public sealed record SubscriptionPurchaseInitiatedEvent
{
    /// <summary>
    /// Correlation ID for the entire purchase flow (purchase → payment → activation).
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// User initiating the subscription purchase.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// ID of the saved payment method to use.
    /// </summary>
    public Guid PaymentMethodId { get; init; }

    /// <summary>
    /// Subscription tier being purchased.
    /// </summary>
    public SubscriptionTier SubscriptionTier { get; init; }

    /// <summary>
    /// Duration of the subscription in days.
    /// </summary>
    public int DurationDays { get; init; }

    /// <summary>
    /// Payment amount for the subscription.
    /// </summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// ISO 4217 currency code.
    /// </summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>
    /// Idempotency key for preventing duplicate purchases.
    /// </summary>
    public string IdempotencyKey { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when purchase was initiated.
    /// </summary>
    public DateTime InitiatedAtUtc { get; init; }
}
