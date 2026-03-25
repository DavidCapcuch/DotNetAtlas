using System.ComponentModel.DataAnnotations;

namespace Weather.Api.Endpoints.Dev.AlertSubscriptions.Extension.PublishAlertSubscriptionExtensionInitiatedEvent;

/// <summary>
/// Command to publish an AlertSubscriptionExtensionInitiatedEvent for dev testing.
/// Simulates what the Order service would emit when a user initiates an alert subscription extension.
/// </summary>
public class PublishAlertSubscriptionExtensionInitiatedEventCommand
{
    /// <summary>
    /// Unique identifier of the alert subscription order. Used as the saga CorrelationId by downstream workflow consumers.
    /// </summary>
    [Required]
    public required Guid AlertSubscriptionOrderId { get; set; }

    /// <summary>
    /// User initiating the subscription extension.
    /// </summary>
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// ID of the saved payment method to use.
    /// </summary>
    [Required]
    public required Guid PaymentMethodId { get; set; }

    /// <summary>
    /// Duration to extend the subscription in days.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int DurationDays { get; set; }

    /// <summary>
    /// Payment amount for the extension.
    /// </summary>
    [Required]
    [Range(0.01, double.MaxValue)]
    public required decimal Amount { get; set; }

    /// <summary>
    /// ISO 4217 currency code (e.g., 'USD', 'EUR').
    /// </summary>
    [Required]
    [StringLength(3)]
    public required string Currency { get; set; }
}
