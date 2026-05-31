using System.ComponentModel.DataAnnotations;

namespace Weather.Api.Endpoints.Dev.Payments.PublishRequestPaymentCommand;

/// <summary>
/// Request DTO for the dev endpoint that publishes a <c>RequestPaymentCommand</c> (renamed from
/// <c>PaymentRequestedEvent</c> per ADR-0023; the wire shape is identical) to
/// <c>payments.payment-commands</c> — simulates what the Checkout saga would emit to initiate
/// the PaymentProcessingSaga sub-saga.
/// </summary>
public class PublishRequestPaymentCommandRequest
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
    /// User for whom payment was requested.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// Gateway-issued opaque payment-method token (Stripe 'pm_*', Adyen alphanumeric, …);
    /// 1-64 chars. Changed from <c>Guid</c> in the Wave-1 closeout C-2 fix.
    /// </summary>
    [Required]
    [MaxLength(64)]
    public required string PaymentMethodId { get; set; }

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
