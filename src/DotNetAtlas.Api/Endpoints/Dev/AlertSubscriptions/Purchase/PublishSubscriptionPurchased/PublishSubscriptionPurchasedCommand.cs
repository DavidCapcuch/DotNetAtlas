using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Api.Endpoints.Dev.AlertSubscriptions.Purchase.PublishSubscriptionPurchased;

/// <summary>
/// Command to publish an ActivateSubscriptionCommand for dev testing.
/// Simulates what the Purchase Saga would emit when requesting subscription activation.
/// </summary>
public class PublishSubscriptionPurchasedCommand
{
    /// <summary>
    /// Saga correlation ID for tracking the subscription purchase flow.
    /// </summary>
    [Required]
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// User ID who purchased the subscription.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// Payment transaction ID from the payment saga.
    /// Used for compensation (refunds) if activation fails.
    /// </summary>
    [Required]
    public required Guid PaymentTransactionId { get; set; }

    /// <summary>
    /// SubscriptionTier purchased.
    /// </summary>
    [Required]
    public required global::Weather.Alerts.SubscriptionTier SubscriptionTier { get; set; }

    /// <summary>
    /// Duration of the subscription in days.
    /// </summary>
    [Required]
    [Range(1, 365)]
    public required int DurationDays { get; set; }
}
