using System.ComponentModel.DataAnnotations;

namespace Weather.Api.Endpoints.Dev.Payments.PublishPaymentVoidedEvent;

/// <summary>
/// Command to publish PaymentVoidedEvent for dev testing.
/// </summary>
public class PublishPaymentVoidedEventCommand
{
    /// <summary>
    /// Correlation ID for tracking the workflow.
    /// </summary>
    [Required]
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// User whose payment was voided.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// Authorization ID that was voided.
    /// </summary>
    [Required]
    public required string AuthorizationId { get; set; }
}
