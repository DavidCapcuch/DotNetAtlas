using Ardalis.SmartEnum;

namespace Invoicing.Domain.Invoices.ValueObjects;

/// <summary>
/// How an invoice is delivered to the buyer. v1 supports email only; the webhook slot is
/// reserved for v2 tax-authority integration per <c>invoicing.md § 17</c>.
/// </summary>
public sealed class DeliveryChannel : SmartEnum<DeliveryChannel>
{
    /// <summary>No delivery requested \u2014 reserved for internally-generated invoices (self-service download).</summary>
    public static readonly DeliveryChannel None = new(nameof(None), 0);

    /// <summary>Default v1 channel \u2014 email the buyer the presigned PDF URL.</summary>
    public static readonly DeliveryChannel Email = new(nameof(Email), 1);

    /// <summary>v2 slot \u2014 tax-authority webhook (SII / XRechnung style). Not implemented in v1.</summary>
    public static readonly DeliveryChannel TaxAuthorityWebhook = new(nameof(TaxAuthorityWebhook), 2);

    private DeliveryChannel(string name, int value)
        : base(name, value)
    {
    }
}
