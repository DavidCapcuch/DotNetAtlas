using System.ComponentModel.DataAnnotations;

namespace Weather.Api.Endpoints.Dev.Payments.PublishPaymentRefundedEvent;

/// <summary>
/// Command to publish PaymentRefundedEvent for dev testing.
/// </summary>
public class PublishPaymentRefundedEventCommand
{
    /// <summary>
    /// Correlation ID for tracking the workflow.
    /// </summary>
    [Required]
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// User whose payment was refunded.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// Original payment transaction ID that was refunded.
    /// </summary>
    [Required]
    public required Guid PaymentTransactionId { get; set; }

    /// <summary>
    /// Refund ID from the payment provider.
    /// </summary>
    [Required]
    public required string RefundId { get; set; }

    /// <summary>
    /// Refund transaction ID for the refund.
    /// </summary>
    [Required]
    public required Guid RefundTransactionId { get; set; }

    /// <summary>
    /// Amount that was refunded.
    /// </summary>
    [Required]
    [Range(0.01, double.MaxValue)]
    public required decimal RefundedAmount { get; set; }

    /// <summary>
    /// ISO 4217 currency code.
    /// </summary>
    [Required]
    [StringLength(3)]
    public required string Currency { get; set; }
}
