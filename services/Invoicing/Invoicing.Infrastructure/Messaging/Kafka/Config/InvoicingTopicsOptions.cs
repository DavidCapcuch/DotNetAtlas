using System.ComponentModel.DataAnnotations;

namespace Invoicing.Infrastructure.Messaging.Kafka.Config;

/// <summary>
/// Names of inbound Kafka topics consumed by the Invoicing enrichment
/// projection. Owned by Ordering and Payments respectively per
/// <c>events-catalog.md § 2</c>; Invoicing only subscribes.
/// </summary>
/// <remarks>
/// The DLT suffix is shared with the (future M7) outbox publisher; landing
/// it in M6 keeps the topic naming convention in one place from the start.
/// </remarks>
public sealed class InvoicingTopicsOptions
{
    public const string Section = "InvoicingTopics";

    /// <summary>Inbound topic owned by Ordering — carries <c>OrderConfirmedEvent</c> + <c>OrderCancelledEvent</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string OrderingOrders { get; set; }

    /// <summary>Inbound topic owned by Payments — carries <c>PaymentCapturedEvent</c> + <c>PaymentRefundedEvent</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string PaymentsTransactions { get; set; }

    /// <summary>Suffix appended to each consumer's DLT (e.g. <c>.Invoicing.DLT</c>).</summary>
    [Required(AllowEmptyStrings = false)]
    public required string DltTopicSuffix { get; set; }
}
