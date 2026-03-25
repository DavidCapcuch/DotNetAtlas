using System.ComponentModel.DataAnnotations;
using Weather.Alerts;

namespace Weather.Api.Endpoints.Dev.AlertSubscriptions.Purchase.PublishSubscriptionActivationFailedEvent;

/// <summary>
/// Command to publish a SubscriptionActivationFailedEvent for dev testing.
/// Simulates what the Weather Alerts service would emit when subscription activation fails.
/// </summary>
public class PublishSubscriptionActivationFailedEventCommand
{
    /// <summary>
    /// Correlation ID for tracking the workflow.
    /// </summary>
    [Required]
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// User whose subscription activation failed.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// Payment transaction ID to correlate with original purchase.
    /// </summary>
    [Required]
    public required Guid PaymentTransactionId { get; set; }

    /// <summary>
    /// Subscription tier that was requested.
    /// </summary>
    [Required]
    public required SubscriptionTier RequestedTier { get; set; }

    /// <summary>
    /// Duration in days that was requested.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int RequestedDurationDays { get; set; }

    /// <summary>
    /// List of errors that caused the activation failure.
    /// </summary>
    [Required]
    [MinLength(1)]
    public required List<ErrorDetailsDto> Errors { get; set; }
}

/// <summary>
/// DTO for ErrorDetails to avoid requiring the Avro-generated type in the API.
/// </summary>
public class ErrorDetailsDto
{
    /// <summary>
    /// Error code identifying the failure reason.
    /// </summary>
    [Required]
    public required string ErrorCode { get; set; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    [Required]
    public required string ErrorMessage { get; set; }
}
