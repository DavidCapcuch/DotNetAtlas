using System.Security.Claims;
using FastEndpoints;
using Ordering.Domain.AlertSubscriptionOrders;
using Platform.CQRS;

namespace Ordering.Application.AlertSubscriptions.PurchaseAlertSubscription;

/// <summary>
/// Command to initiate a new alert subscription purchase.
/// Triggers the Purchase Alert Subscription Saga via an outbox event.
/// </summary>
public class PurchaseAlertSubscriptionCommand : ICommand<Guid>
{
    /// <summary>
    /// ID of the saved payment method to use.
    /// </summary>
    public required Guid PaymentMethodId { get; set; }

    /// <summary>
    /// Subscription tier being purchased (Pro or Ultra).
    /// </summary>
    public required AlertSubscriptionTier Tier { get; set; }

    /// <summary>
    /// Duration of the subscription in days.
    /// </summary>
    public required int DurationDays { get; set; }

    /// <summary>
    /// Payment amount for the subscription.
    /// </summary>
    public required decimal Amount { get; set; }

    /// <summary>
    /// ISO 4217 currency code (e.g., 'USD', 'EUR').
    /// </summary>
    public required string Currency { get; set; }

    /// <summary>
    /// User ID extracted from the JWT token.
    /// </summary>
    [FromClaim(ClaimTypes.NameIdentifier, isRequired: true, removeFromSchema: true)]
    [HideFromDocs]
    public Guid UserId { get; set; }
}
