using System.ComponentModel.DataAnnotations;

namespace Weather.Api.Endpoints.Dev.Payments.PublishRequestRefund;

/// <summary>
/// Command to publish a RequestRefundCommand for dev testing.
/// Simulates what the Payment Saga would send to request a refund.
/// </summary>
public class PublishRequestRefundCommand
{
    /// <summary>
    /// Correlation ID for tracking the workflow.
    /// </summary>
    [Required]
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// User to refund.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// Original payment transaction ID to refund.
    /// </summary>
    [Required]
    public required Guid PaymentTransactionId { get; set; }

    /// <summary>
    /// Reason for the refund request.
    /// </summary>
    [Required]
    public required string Reason { get; set; }
}
