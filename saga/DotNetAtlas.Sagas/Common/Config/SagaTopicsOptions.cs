using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Sagas.Common.Config;

/// <summary>
/// Topic configuration for saga events.
/// </summary>
public sealed class SagaTopicsOptions
{
    /// <summary>
    /// Topic for Order alert subscription events (AlertSubscriptionPurchaseInitiatedEvent, AlertSubscriptionExtensionInitiatedEvent).
    /// Consumed by Purchase saga and Extension saga to start their workflows.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string OrderAlertSubscriptions { get; set; }

    /// <summary>
    /// Topic for Weather Alerts events (SubscriptionActivatedEvent, SubscriptionExtendedEvent).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string WeatherAlerts { get; set; }

    /// <summary>
    /// Topic for Finance payment events (PaymentRequestedEvent, PaymentAuthorizedEvent, PaymentCapturedEvent, PaymentRefundedEvent, etc.).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string FinancePayments { get; set; }

    /// <summary>
    /// Topic for Finance payment commands (AuthorizePaymentCommand, CapturePaymentCommand, VoidPaymentCommand, RequestRefundCommand).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string FinancePaymentCommands { get; set; }

    /// <summary>
    /// Topic for Weather Alerts commands (ActivateSubscriptionCommand, ExtendSubscriptionCommand).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string WeatherAlertsCommands { get; set; }
}
