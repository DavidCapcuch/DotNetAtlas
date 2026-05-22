using System.ComponentModel.DataAnnotations;

namespace Invoicing.Application.Common.Notifications;

/// <summary>
/// Configuration for the buyer-portal URL embedded in delivery-notification emails.
/// Production points at the buyer portal frontend host; dev defaults to the Invoicing API
/// itself (clicking through hits the existing GET endpoint that mints a SAS server-side).
/// </summary>
public sealed class BuyerPortalOptions
{
    public const string Section = "BuyerPortal";

    [Required(AllowEmptyStrings = false)]
    [Url]
    public required string BaseUrl { get; set; }
}
