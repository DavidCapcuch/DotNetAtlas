using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Api.Endpoints.Dev.Payments.PublishCapturePayment;

/// <summary>
/// Command to publish a CapturePaymentCommand for dev testing.
/// Simulates what the Payment Saga would send to capture an authorized payment.
/// </summary>
public class PublishCapturePaymentCommand
{
    /// <summary>
    /// Correlation ID for tracking the workflow.
    /// </summary>
    [Required]
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// User whose payment to capture.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// Authorization ID from the payment provider to capture.
    /// </summary>
    [Required]
    public required string AuthorizationId { get; set; }

    /// <summary>
    /// Amount to capture (may be less than or equal to authorized amount).
    /// </summary>
    [Required]
    [Range(0.01, double.MaxValue)]
    public required decimal Amount { get; set; }
}
