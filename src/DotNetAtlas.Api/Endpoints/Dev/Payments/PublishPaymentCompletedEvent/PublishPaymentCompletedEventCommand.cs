using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Api.Endpoints.Dev.Payments.PublishPaymentCompletedEvent;

/// <summary>
/// Command to publish PaymentCompletedEvent for dev testing.
/// </summary>
public class PublishPaymentCompletedEventCommand
{
    /// <summary>
    /// Correlation ID for tracking the workflow.
    /// </summary>
    [Required]
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// User whose payment was completed.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// Payment transaction ID that was completed.
    /// </summary>
    [Required]
    public required Guid PaymentTransactionId { get; set; }

    /// <summary>
    /// Amount that was charged.
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
