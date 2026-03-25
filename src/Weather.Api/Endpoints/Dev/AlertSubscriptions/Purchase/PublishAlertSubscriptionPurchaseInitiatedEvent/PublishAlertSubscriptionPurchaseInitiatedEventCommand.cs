using System.ComponentModel.DataAnnotations;
using Order.AlertSubscriptions;

namespace Weather.Api.Endpoints.Dev.AlertSubscriptions.Purchase.PublishAlertSubscriptionPurchaseInitiatedEvent;

/// <summary>
/// Command to publish an AlertSubscriptionPurchaseInitiatedEvent for dev testing.
/// Simulates what the Order service would emit when a user initiates a new alert subscription purchase.
/// </summary>
public class PublishAlertSubscriptionPurchaseInitiatedEventCommand
{
    /// <summary>
    /// Unique identifier of the alert subscription order. Used as the saga CorrelationId by downstream workflow consumers.
    /// </summary>
    [Required]
    public required Guid AlertSubscriptionOrderId { get; set; }

    /// <summary>
    /// User initiating the subscription purchase.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// ID of the saved payment method to use.
    /// </summary>
    [Required]
    public required Guid PaymentMethodId { get; set; }

    /// <summary>
    /// Subscription tier being purchased.
    /// </summary>
    [Required]
    public required SubscriptionTier Tier { get; set; }

    /// <summary>
    /// Duration of the subscription in days.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int DurationDays { get; set; }

    /// <summary>
    /// Payment amount for the subscription.
    /// </summary>
    [Required]
    [Range(0.01, double.MaxValue)]
    public required decimal Amount { get; set; }

    /// <summary>
    /// ISO 4217 currency code (e.g., 'USD', 'EUR').
    /// </summary>
    [Required]
    [StringLength(3)]
    public required string Currency { get; set; }
}
