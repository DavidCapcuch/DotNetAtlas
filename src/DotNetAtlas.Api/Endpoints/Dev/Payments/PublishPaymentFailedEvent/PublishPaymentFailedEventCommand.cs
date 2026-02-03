using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Api.Endpoints.Dev.Payments.PublishPaymentFailedEvent;

/// <summary>
/// Command to publish PaymentFailedEvent for dev testing.
/// </summary>
public class PublishPaymentFailedEventCommand
{
    /// <summary>
    /// Correlation ID for tracking the workflow.
    /// </summary>
    [Required]
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// User whose payment failed.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// Error code from the payment provider (e.g., 'INSUFFICIENT_FUNDS', 'CARD_DECLINED', 'FRAUD_SUSPECTED').
    /// </summary>
    [Required]
    public required string ErrorCode { get; set; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    [Required]
    public required string ErrorMessage { get; set; }
}
