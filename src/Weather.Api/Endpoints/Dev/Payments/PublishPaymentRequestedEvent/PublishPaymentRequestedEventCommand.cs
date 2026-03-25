using System.ComponentModel.DataAnnotations;

namespace Weather.Api.Endpoints.Dev.Payments.PublishPaymentRequestedEvent;

/// <summary>
/// Command to publish PaymentRequestedEvent for dev testing.
/// </summary>
public class PublishPaymentRequestedEventCommand
{
    /// <summary>
    /// Correlation ID for tracking the workflow.
    /// </summary>
    [Required]
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// User for whom payment was requested.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// ID of the saved payment method to use for this transaction.
    /// </summary>
    [Required]
    public required Guid PaymentMethodId { get; set; }

    /// <summary>
    /// Amount that was requested.
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

    /// <summary>
    /// Idempotency key for preventing duplicate payment processing.
    /// </summary>
    [Required]
    public required string IdempotencyKey { get; set; }
}
