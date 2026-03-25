using System.ComponentModel.DataAnnotations;
using Weather.Api.Endpoints.Dev.AlertSubscriptions.Purchase.PublishSubscriptionActivationFailedEvent;

namespace Weather.Api.Endpoints.Dev.AlertSubscriptions.Extension.PublishSubscriptionExtensionActivationFailedEvent;

/// <summary>
/// Command to publish a SubscriptionExtensionActivationFailedEvent for dev testing.
/// Simulates what the Weather Alerts service would emit when subscription extension activation fails.
/// </summary>
public class PublishSubscriptionExtensionActivationFailedEventCommand
{
    /// <summary>
    /// Correlation ID for tracking the workflow.
    /// </summary>
    [Required]
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// User whose subscription extension activation failed.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// Payment transaction ID to correlate with original extension.
    /// </summary>
    [Required]
    public required Guid PaymentTransactionId { get; set; }

    /// <summary>
    /// Duration of the subscription extension in days that was requested.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int RequestedDurationExtendedDays { get; set; }

    /// <summary>
    /// List of errors that caused the extension activation failure.
    /// </summary>
    [Required]
    [MinLength(1)]
    public required List<ErrorDetailsDto> Errors { get; set; }
}
