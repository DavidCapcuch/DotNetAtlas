using DotNetAtlas.SharedKernel.Base;

namespace Ordering.Domain.AlertSubscriptionOrders;

/// <summary>
/// Represents a subscription order for alert subscription purchase or extension.
/// Tracks the order lifecycle from initiation through completion.
/// </summary>
public class AlertSubscriptionOrder : AggregateRoot<Guid>
{
    /// <summary>
    /// User who initiated the order.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Type of order: Purchase or Extension.
    /// </summary>
    public AlertSubscriptionOrderType AlertSubscriptionOrderType { get; set; }

    /// <summary>
    /// ID of the saved payment method to use.
    /// </summary>
    public Guid PaymentMethodId { get; set; }

    /// <summary>
    /// Subscription tier (only for purchases; null for extensions).
    /// </summary>
    public string? Tier { get; set; }

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
    public string Currency { get; set; } = null!;

    /// <summary>
    /// Idempotency key for preventing duplicate orders.
    /// </summary>
    public string IdempotencyKey { get; set; } = null!;

    /// <summary>
    /// Current status of the order.
    /// </summary>
    public AlertSubscriptionOrderStatus Status { get; set; }

    /// <summary>
    /// UTC timestamp when the order was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }
}
