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
            + "halves keyed on OrderId until IssueCreditNoteCommandHandler converts "
            + "the converged row into a CreditNote aggregate."));

        builder.HasKey(r => r.OrderId);

        builder.Property(r => r.OrderId)
            .ValueGeneratedNever()
            .HasComment("OrderCancelledEvent.OrderId; the cross-BC convergence key. Primary key.");

        builder.Property(r => r.PaymentId)
            .HasComment("PaymentRefundedEvent.PaymentTransactionId — the original captured payment, not the refund txn id.");

        builder.Property(r => r.BuyerId)
            .HasComment("OrderCancelledEvent.BuyerId; the outbox publisher uses this as the partition key.");

        builder.Property(r => r.OrderPayload)
            .HasColumnType("jsonb")
            .HasComment("PII: full OrderCancelledEvent serialised to JSON for issuance-time hydration.");

        builder.Property(r => r.PaymentPayload)
            .HasColumnType("jsonb")
            .HasComment("Full PaymentRefundedEvent serialised to JSON for issuance-time hydration.");

        builder.Property(r => r.FirstSeenAtUtc)
            .IsRequired()
            .HasComment("Wall-clock at first observation; never overwritten.");

        builder.Property(r => r.CompletedAtUtc)
            .HasComment("Set when both halves are present.");

        builder.Property(r => r.IssuedCreditNoteId)
            .HasComment("Set by IssueCreditNoteCommandHandler atomically with the CreditNote aggregate insert.");

        builder.HasIndex(r => new { r.CompletedAtUtc, r.IssuedCreditNoteId })
            .HasDatabaseName("ix_pending_credit_notes_ready");
    }
}
