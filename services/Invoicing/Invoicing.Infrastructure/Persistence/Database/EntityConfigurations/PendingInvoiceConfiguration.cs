using Invoicing.Application.Invoices.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Invoicing.Infrastructure.Persistence.Database.EntityConfigurations;

/// <summary>
/// EF mapping for <see cref="PendingInvoice"/> per <c>docs/bc-design/invoicing.md § 8.1</c>.
/// PK on <c>order_id</c> keeps consumer upserts atomic on a single row.
/// jsonb columns hold raw Avro→JSON envelopes for the issuance command handler to rehydrate.
/// </summary>
internal sealed class PendingInvoiceConfiguration : IEntityTypeConfiguration<PendingInvoice>
{
    public void Configure(EntityTypeBuilder<PendingInvoice> builder)
    {
        builder.ToTable("pending_invoices", t => t.HasComment(
            "Async-enrichment buffer: collects OrderConfirmedEvent + PaymentCapturedEvent "
            + "halves keyed on OrderId until IssueInvoiceCommandHandler converts "
            + "the converged row into an Invoice aggregate."));

        builder.HasKey(r => r.OrderId);

        builder.Property(r => r.OrderId)
            .ValueGeneratedNever()
            .HasComment("OrderConfirmedEvent.OrderId; the cross-BC convergence key. Primary key.");

        builder.Property(r => r.PaymentId)
            .HasComment("PaymentCapturedEvent.PaymentTransactionId; null until the payment half arrives.");

        builder.Property(r => r.BuyerId)
            .HasComment("OrderConfirmedEvent.BuyerId; the outbox publisher uses this as the partition key on invoicing.invoices.");

        builder.Property(r => r.OrderPayload)
            .HasColumnType("jsonb")
            .HasComment("PII: full OrderConfirmedEvent serialised to JSON for issuance-time hydration.");

        builder.Property(r => r.PaymentPayload)
            .HasColumnType("jsonb")
            .HasComment("PII: full PaymentCapturedEvent serialised to JSON for issuance-time hydration.");

        builder.Property(r => r.FirstSeenAtUtc)
            .IsRequired()
            .HasComment("Wall-clock at first observation; never overwritten on subsequent updates.");

        builder.Property(r => r.CompletedAtUtc)
            .HasComment("Set when both halves are present.");

        builder.Property(r => r.IssuedInvoiceId)
            .HasComment("Set by IssueInvoiceCommandHandler atomically with the Invoice aggregate insert.");

        // Scan for ready-but-unissued rows uses this index.
        builder.HasIndex(r => new { r.CompletedAtUtc, r.IssuedInvoiceId })
            .HasDatabaseName("ix_pending_invoices_ready");
    }
}
