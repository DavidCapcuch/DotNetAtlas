using Invoicing.Application.CreditNotes.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Invoicing.Infrastructure.Persistence.Database.EntityConfigurations;

/// <summary>
/// EF mapping for <see cref="PendingCreditNote"/> per
/// <c>docs/bc-design/invoicing.md § 8.3</c>. Mirrors <see cref="PendingInvoiceConfiguration"/>;
/// keep the two in sync if the schema evolves.
/// </summary>
internal sealed class PendingCreditNoteConfiguration : IEntityTypeConfiguration<PendingCreditNote>
{
    public void Configure(EntityTypeBuilder<PendingCreditNote> builder)
    {
        builder.ToTable("pending_credit_notes", t => t.HasComment(
            "Async-enrichment buffer: collects OrderCancelledEvent + PaymentRefundedEvent "
            + "halves keyed on CorrelationId until M7's IssueCreditNoteCommandHandler converts "
            + "the converged row into a CreditNote aggregate."));

        builder.HasKey(r => r.CorrelationId);

        builder.Property(r => r.CorrelationId)
            .ValueGeneratedNever()
            .HasComment("Saga / cross-BC correlation id. Primary key.");

        builder.Property(r => r.OrderId)
            .HasComment("OrderCancelledEvent.OrderId; null until the order-cancel half arrives.");

        builder.Property(r => r.PaymentId)
            .HasComment("PaymentRefundedEvent.PaymentTransactionId — the original captured payment, not the refund txn id.");

        builder.Property(r => r.BuyerId)
            .HasComment("OrderCancelledEvent.BuyerId; M7's outbox publisher uses this as the partition key.");

        builder.Property(r => r.OrderPayload)
            .HasColumnType("jsonb")
            .HasComment("PII: full OrderCancelledEvent serialised to JSON for M7 hydration.");

        builder.Property(r => r.PaymentPayload)
            .HasColumnType("jsonb")
            .HasComment("Full PaymentRefundedEvent serialised to JSON for M7 hydration.");

        builder.Property(r => r.FirstSeenAtUtc)
            .IsRequired()
            .HasComment("Wall-clock at first observation; never overwritten.");

        builder.Property(r => r.CompletedAtUtc)
            .HasComment("Set when both halves are present.");

        builder.Property(r => r.IssuedCreditNoteId)
            .HasComment("Set by M7's IssueCreditNoteCommandHandler atomically with the CreditNote aggregate insert.");

        builder.HasIndex(r => new { r.CompletedAtUtc, r.IssuedCreditNoteId })
            .HasDatabaseName("ix_pending_credit_notes_ready");

        builder.HasIndex(r => r.OrderId)
            .HasDatabaseName("ix_pending_credit_notes_order_id");
    }
}
