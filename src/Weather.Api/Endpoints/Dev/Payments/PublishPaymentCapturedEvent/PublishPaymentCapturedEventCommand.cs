using System.ComponentModel.DataAnnotations;

namespace Weather.Api.Endpoints.Dev.Payments.PublishPaymentCapturedEvent;

/// <summary>
/// Command to publish PaymentCapturedEvent for dev testing.
/// </summary>
public class PublishPaymentCapturedEventCommand
{
    /// <summary>
    /// Correlation ID for tracking the workflow.
    /// </summary>
    [Required]
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// User whose payment was captured.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// Capture ID from the payment provider.
    /// </summary>
    [Required]
    public required string CaptureId { get; set; }

    /// <summary>
    /// Authorization ID that was captured.
    /// </summary>
    [Required]
    public required string AuthorizationId { get; set; }

    /// <summary>
    /// Amount that was captured.
    /// </summary>
    [Required]
    [Range(0.01, double.MaxValue)]
    public required decimal Amount { get; set; }

    /// <summary>
    /// ISO 4217 currency code.
    /// </summary>
    [Required]
    [StringLength(3)]
    public required string Currency { get; set; }
}
