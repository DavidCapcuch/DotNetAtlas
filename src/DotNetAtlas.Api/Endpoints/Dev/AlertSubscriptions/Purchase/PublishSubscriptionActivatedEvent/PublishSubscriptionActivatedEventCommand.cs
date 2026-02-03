using System.ComponentModel.DataAnnotations;
using Weather.Alerts;

namespace DotNetAtlas.Api.Endpoints.Dev.AlertSubscriptions.Purchase.PublishSubscriptionActivatedEvent;

/// <summary>
/// Command to publish a SubscriptionActivatedEvent for dev testing.
/// Simulates what the Weather Alerts service would emit when subscription activation succeeds.
/// </summary>
public class PublishSubscriptionActivatedEventCommand
{
    /// <summary>
    /// Correlation ID for tracking the workflow.
    /// </summary>
    [Required]
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// User whose subscription was activated.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// Payment transaction ID for saga correlation.
    /// </summary>
    [Required]
    public required Guid PaymentTransactionId { get; set; }

    /// <summary>
    /// Subscription tier that was activated.
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
    /// UTC timestamp when the subscription expires.
    /// </summary>
    [Required]
    public required DateTime ExpiresAtUtc { get; set; }
}
