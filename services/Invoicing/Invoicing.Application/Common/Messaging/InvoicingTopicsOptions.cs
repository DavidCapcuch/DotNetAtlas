using System.ComponentModel.DataAnnotations;

namespace Invoicing.Application.Common.Messaging;

/// <summary>
/// Kafka topic names for the Invoicing bounded context — both the inbound topics it
/// subscribes to (Ordering + Payments) and the outbound topic the M7 outbox publishers
/// emit to (<see cref="Invoices"/>). Bound from configuration section <c>InvoicingTopics</c>
/// on startup; validated eagerly via <c>AddOptionsWithValidateOnStart</c>.
/// </summary>
/// <remarks>
/// <para>
/// Lives in the Application layer (per <c>_shared.md § 5</c>) so the outbox publisher
/// domain-event handlers can read it without taking a dependency on Infrastructure-namespace
/// types. The four enrichment-projection consumers in Infrastructure also bind against
/// this same options object — there is exactly one source of truth for Invoicing topic names.
/// </para>
/// <para>
/// Outbound topic <c>invoicing.invoices</c> has 10-year retention per EU VAT norms (see
/// <c>docs/bc-design/invoicing.md § 6</c>); inbound topic retention is owned by the producing
/// BC. The DLT suffix is shared by the consumer DLT producer and the outbox-relay's failed
/// envelopes.
/// </para>
/// </remarks>
public sealed class InvoicingTopicsOptions
{
    public const string Section = "InvoicingTopics";
    private const int MaximumKafkaTopicLength = 249;

    /// <summary>
    /// Outbound topic carrying Invoicing's external events — <c>InvoiceIssuedEvent</c>,
    /// <c>InvoiceCancelledEvent</c>, <c>CreditNoteIssuedEvent</c> (M7) and <c>InvoiceDeliveredEvent</c>
    /// (M8). Single topic, partition key <c>BuyerId</c>, 10-year retention.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [Length(1, MaximumKafkaTopicLength)]
    public required string Invoices { get; set; }

    /// <summary>Inbound topic owned by Ordering — carries <c>OrderConfirmedEvent</c> + <c>OrderCancelledEvent</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    [Length(1, MaximumKafkaTopicLength)]
    public required string OrderingOrders { get; set; }

    /// <summary>Inbound topic owned by Payments — carries <c>PaymentCapturedEvent</c> + <c>PaymentRefundedEvent</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    [Length(1, MaximumKafkaTopicLength)]
    public required string PaymentsTransactions { get; set; }

    /// <summary>Suffix appended to each consumer's DLT (e.g. <c>.Invoicing.DLT</c>).</summary>
    [Required(AllowEmptyStrings = false)]
    [Length(1, 64)]
    public required string DltTopicSuffix { get; set; }
}
