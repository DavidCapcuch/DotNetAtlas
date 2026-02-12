using Order.AlertSubscriptions;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event that initiates the subscription purchase saga.
/// Contains business context for the subscription purchase.
/// </summary>
public sealed record AlertSubscriptionPurchaseInitiatedSagaEvent
{
    /// <summary>
    /// Correlation ID for the entire purchase flow (purchase → payment → activation).
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// User initiating the subscription purchase.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// ID of the saved payment method to use.
    /// </summary>
    public required Guid PaymentMethodId { get; init; }

    /// <summary>
    /// Subscription tier being purchased.
    /// </summary>
    public required SubscriptionTier SubscriptionTier { get; init; }

    /// <summary>
    /// Duration of the subscription in days.
    /// </summary>
    public required int DurationDays { get; init; }

    /// <summary>
    /// Payment amount for the subscription.
    /// </summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// ISO 4217 currency code.
    /// </summary>
    public required string Currency { get; init; }

    /// <summary>
    /// UTC timestamp when purchase was initiated.
    /// </summary>
    public required DateTime InitiatedAtUtc { get; init; }
}
