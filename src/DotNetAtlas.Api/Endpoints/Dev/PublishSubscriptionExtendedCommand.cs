using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Api.Endpoints.Dev;

/// <summary>
/// Command to publish an ExtendSubscriptionCommand for dev testing.
/// Simulates what the Extension Saga would emit when requesting subscription extension.
/// </summary>
public class PublishSubscriptionExtendedCommand
{
    /// <summary>
    /// Saga correlation ID for tracking the subscription extension flow.
    /// </summary>
    [Required]
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// User ID who extended the subscription.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// Payment transaction ID from the payment saga.
    /// Used for compensation (refunds) if extension activation fails.
    /// </summary>
    [Required]
    public required Guid PaymentTransactionId { get; set; }

    /// <summary>
    /// Duration to extend the subscription in days.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int DurationExtendedDays { get; set; }
}
