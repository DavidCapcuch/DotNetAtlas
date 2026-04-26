using Invoicing.Application.Invoices.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Invoicing.Infrastructure.Persistence.Database.EntityConfigurations;

/// <summary>
/// EF mapping for <see cref="PendingInvoice"/> per <c>docs/bc-design/invoicing.md § 8.1</c>.
/// PK on <c>correlation_id</c> keeps consumer upserts atomic on a single row.
/// jsonb columns hold raw Avro→JSON envelopes for M7 to rehydrate.
/// </summary>
internal sealed class PendingInvoiceConfiguration : IEntityTypeConfiguration<PendingInvoice>
{
    public void Configure(EntityTypeBuilder<PendingInvoice> builder)
    {
        builder.ToTable("pending_invoices", t => t.HasComment(
            "Async-enrichment buffer: collects OrderConfirmedEvent + PaymentCapturedEvent "
            + "halves keyed on CorrelationId until M7's IssueInvoiceCommandHandler converts "
            + "the converged row into an Invoice aggregate."));

        builder.HasKey(r => r.CorrelationId);

        builder.Property(r => r.CorrelationId)
            .ValueGeneratedNever()
            .HasComment("Saga / cross-BC correlation id. Primary key.");

        builder.Property(r => r.OrderId)
            .HasComment("OrderConfirmedEvent.OrderId; null until the order half arrives.");

        builder.Property(r => r.PaymentId)
            .HasComment("PaymentCapturedEvent.PaymentTransactionId; null until the payment half arrives.");

        builder.Property(r => r.BuyerId)
            .HasComment("OrderConfirmedEvent.BuyerId; M7's outbox publisher uses this as the partition key on invoicing.invoices.");

        builder.Property(r => r.OrderPayload)
            .HasColumnType("jsonb")
            .HasComment("PII: full OrderConfirmedEvent serialised to JSON for M7 hydration.");

        builder.Property(r => r.PaymentPayload)
            .HasColumnType("jsonb")
            .HasComment("PII: full PaymentCapturedEvent serialised to JSON for M7 hydration.");

        builder.Property(r => r.FirstSeenAtUtc)
            .IsRequired()
            .HasComment("Wall-clock at first observation; never overwritten on subsequent updates.");

        builder.Property(r => r.CompletedAtUtc)
            .HasComment("Set when both halves are present.");

        builder.Property(r => r.IssuedInvoiceId)
            .HasComment("Set by M7's IssueInvoiceCommandHandler atomically with the Invoice aggregate insert.");

        // M7 will scan for ready-but-unissued rows on this index.
        builder.HasIndex(r => new { r.CompletedAtUtc, r.IssuedInvoiceId })
            .HasDatabaseName("ix_pending_invoices_ready");

        // M7's GET /invoices/by-order/{orderId} short-path may key by OrderId before the aggregate is queryable.
        builder.HasIndex(r => r.OrderId)
            .HasDatabaseName("ix_pending_invoices_order_id");
    }
}
