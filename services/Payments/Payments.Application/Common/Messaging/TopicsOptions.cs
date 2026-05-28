using System.ComponentModel.DataAnnotations;

namespace Payments.Application.Common.Messaging;

/// <summary>
/// Kafka topic names for the Payments BC's outbox publishing. Bound from configuration section
/// <see cref="Section"/>. The single topic emitted by Payments is <c>payments.transactions</c>
/// (per <c>events-catalog.md § 2</c>); the command-intake topic <c>payments.payment-commands</c>
/// is owned by the Kafka consumer wiring.
/// </summary>
public sealed class TopicsOptions
{
    /// <summary>Configuration section name (BC-prefixed for clarity in shared composition roots).</summary>
    public const string Section = "Topics";

    private const int MaximumKafkaTopicLength = 249;

    /// <summary>
    /// Topic for Payments lifecycle events (authorize / capture / void / refund + their failures).
    /// Consumed by PaymentProcessingSaga, Checkout saga, Notifications, and Invoicing.
    /// Default in appsettings: <c>payments.transactions</c>.
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string Transactions { get; set; }

    /// <summary>
    /// Suffix appended to topic names to produce Dead Letter Topic names (e.g., <c>.DLT</c>).
    /// </summary>
    [Required]
    [Length(1, 64)]
    public required string DltTopicSuffix { get; set; }
}
