using System.ComponentModel.DataAnnotations;

namespace Weather.Api.Endpoints.Dev.Payments.PublishPaymentCaptureFailedEvent;

/// <summary>
/// Command to publish PaymentCaptureFailedEvent for dev testing.
/// </summary>
public class PublishPaymentCaptureFailedEventCommand
{
    /// <summary>
    /// Correlation ID for tracking the workflow.
    /// </summary>
    [Required]
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// User whose payment capture failed.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// Authorization ID that failed to capture.
    /// </summary>
    [Required]
    public required string AuthorizationId { get; set; }

    /// <summary>
    /// Error code from the payment provider.
    /// </summary>
    [Required]
    public required string ErrorCode { get; set; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    [Required]
    public required string ErrorMessage { get; set; }

    /// <summary>
    /// Indicates whether this failure is retryable.
    /// </summary>
    [Required]
    public required bool IsRetryable { get; set; }
}
