using System.ComponentModel.DataAnnotations;

namespace Weather.Api.Endpoints.Dev.Payments.PublishAuthorizePayment;

/// <summary>
/// Command to publish an AuthorizePaymentCommand for dev testing.
/// Simulates what the Payment Saga would send to request payment authorization from the Payment Service.
/// </summary>
public class PublishAuthorizePaymentCommand
{
    /// <summary>
    /// Correlation ID for tracking the workflow.
    /// </summary>
    [Required]
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// Ordering aggregate id this payment is attached to.
    /// </summary>
    [Required]
    public required Guid OrderId { get; set; }

    /// <summary>
    /// User to authorize payment for.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// ID of the saved payment method to use.
    /// </summary>
    [Required]
    public required Guid PaymentMethodId { get; set; }

    /// <summary>
    /// Amount to authorize.
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
    /// Idempotency key to prevent duplicate authorizations.
    /// </summary>
    [Required]
    public required string IdempotencyKey { get; set; }
}
