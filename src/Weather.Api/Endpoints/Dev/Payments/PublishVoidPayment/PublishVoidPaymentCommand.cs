using System.ComponentModel.DataAnnotations;

namespace Weather.Api.Endpoints.Dev.Payments.PublishVoidPayment;

/// <summary>
/// Command to publish a VoidPaymentCommand for dev testing.
/// Simulates what the Payment Saga would send to void an authorized payment.
/// </summary>
public class PublishVoidPaymentCommand
{
    /// <summary>
    /// Correlation ID for tracking the workflow.
    /// </summary>
    [Required]
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// User whose payment to void.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// Authorization ID from the payment provider to void.
    /// </summary>
    [Required]
    public required string AuthorizationId { get; set; }

    /// <summary>
    /// Reason for voiding the payment.
    /// </summary>
    [Required]
    public required string Reason { get; set; }
}
