using System.ComponentModel.DataAnnotations;

namespace Weather.Api.Endpoints.Dev.AlertSubscriptions.Extension.PublishSubscriptionExtendedEvent;

/// <summary>
/// Command to publish a SubscriptionExtendedEvent for dev testing.
/// Simulates what the Weather Alerts service would emit when subscription extension succeeds.
/// </summary>
public class PublishSubscriptionExtendedEventCommand
{
    /// <summary>
    /// Correlation ID for tracking the workflow.
    /// </summary>
    [Required]
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// User whose subscription was extended.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// Payment transaction ID for saga correlation.
    /// </summary>
    [Required]
    public required Guid PaymentTransactionId { get; set; }

    /// <summary>
    /// Duration in days that the subscription was extended.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int DurationExtendedDays { get; set; }

    /// <summary>
    /// New UTC timestamp when the subscription expires.
    /// </summary>
    [Required]
    public required DateTime NewExpiresAtUtc { get; set; }
}
