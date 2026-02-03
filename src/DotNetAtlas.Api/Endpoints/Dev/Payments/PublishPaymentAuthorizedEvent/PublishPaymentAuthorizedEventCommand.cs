using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Api.Endpoints.Dev.Payments.PublishPaymentAuthorizedEvent;

/// <summary>
/// Command to publish PaymentAuthorizedEvent for dev testing.
/// </summary>
public class PublishPaymentAuthorizedEventCommand
{
    /// <summary>
    /// Correlation ID for tracking the workflow.
    /// </summary>
    [Required]
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// User whose payment was authorized.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// Authorization ID from the payment provider.
    /// </summary>
    [Required]
    public required string AuthorizationId { get; set; }

    /// <summary>
    /// Amount that was authorized.
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
