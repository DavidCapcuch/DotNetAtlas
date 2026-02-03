using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Api.Endpoints.Dev.Payments.PublishPaymentAuthorizationFailedEvent;

/// <summary>
/// Command to publish PaymentAuthorizationFailedEvent for dev testing.
/// </summary>
public class PublishPaymentAuthorizationFailedEventCommand
{
    /// <summary>
    /// Correlation ID for tracking the workflow.
    /// </summary>
    [Required]
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// User whose payment authorization failed.
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

    /// <summary>
    /// Indicates whether this failure is retryable (e.g., temporary network issue vs. hard decline).
    /// </summary>
    [Required]
    public required bool IsRetryable { get; set; }
}
