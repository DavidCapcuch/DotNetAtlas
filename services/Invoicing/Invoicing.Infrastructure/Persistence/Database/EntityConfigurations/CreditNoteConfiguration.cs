using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.CreditNotes;
using Invoicing.Domain.CreditNotes.ValueObjects;
using Invoicing.Domain.Invoices.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Invoicing.Infrastructure.Persistence.Database.EntityConfigurations;

/// <summary>
/// EF Core mapping for the <see cref="CreditNote"/> aggregate root.
/// <list type="bullet">
/// <item>Postgres <c>xmin</c> system column as the optimistic concurrency token via the
/// inherited <c>Entity.RowVersion</c> property.</item>
/// <item><see cref="CreditNoteNumber"/> persisted as VARCHAR(14) (<c>CN-YYYY-NNNNNN</c>) via value
/// converter; nullable until <see cref="CreditNote.Issue"/> stamps it (the credit note is
/// stamped in the same transaction as creation, but the schema retains nullability for symmetry
/// with <see cref="Invoicing.Domain.Invoices.Invoice"/>).</item>
/// <item><see cref="CreditNote.OriginalInvoiceNumber"/> is required (the credit note always
/// references an issued invoice).</item>
/// <item>Owned <c>credit_note_lines</c> reuses the line-mapping helper from
/// <see cref="InvoiceConfiguration"/>. Amounts can be negative (sign-flipped) so we do NOT add
/// a positive-only check constraint at the DB level.</item>
/// </list>
/// </summary>
internal sealed class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    private const int CreditNoteNumberMaxLength = 14; // CN-YYYY-NNNNNN
    private const int InvoiceNumberMaxLength = 15;
    private const int ContentHashLength = 64;

    public void Configure(EntityTypeBuilder<CreditNote> builder)
    {
        builder.ToTable("credit_notes", t => t.HasComment(
            "CreditNote aggregate — reverses a previously-issued Invoice (sign-flipped lines)."));

        builder.HasKey(cn => cn.Id);
        builder.Property(cn => cn.Id)
            .ValueGeneratedNever()
            .HasComment("Primary key (Guid v7).");

        builder.Property(cn => cn.RowVersion)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasComment("Optimistic concurrency token (Postgres xmin system column).");

        builder.Property(cn => cn.CreditNoteNumber)
            .HasColumnName("credit_note_number")
            .HasMaxLength(CreditNoteNumberMaxLength)
            .HasConversion(
                n => n!.Value,
                s => CreditNoteNumber.FromRaw(s).Value)
            .HasComment("Gap-free credit-note number, format CN-YYYY-NNNNNN.");

        // Unique partial index — defence-in-depth against double-allocation bugs (the allocator
        // already serialises via SELECT ... FOR UPDATE per ADR-0018; this is a backstop).
        builder.HasIndex(cn => cn.CreditNoteNumber)
            .IsUnique()
            .HasFilter("credit_note_number IS NOT NULL")
            .HasDatabaseName("UX_CreditNotes_CreditNoteNumber");

        builder.Property(cn => cn.OriginalInvoiceId)
            .HasComment("Identifier of the Invoice this credit note reverses.");
        builder.HasIndex(cn => cn.OriginalInvoiceId)
            .IsUnique()
            .HasDatabaseName("UX_CreditNotes_OriginalInvoiceId");

        builder.Property(cn => cn.OriginalInvoiceNumber)
            .HasColumnName("original_invoice_number")
            .HasMaxLength(InvoiceNumberMaxLength)
            .IsRequired()
            .HasConversion(
                n => n.Value,
                s => InvoiceNumber.FromRaw(s).Value)
            .HasComment("Snapshot of the original Invoice's number for PDF rendering and reconciliation.");

        builder.Property(cn => cn.BuyerId)
            .HasComment("Buyer of the original invoice (and therefore the credit note).");
        builder.HasIndex(cn => cn.BuyerId).HasDatabaseName("IX_CreditNotes_BuyerId");

        builder.Property(cn => cn.CorrelationId)
            .HasComment("Cancellation flow correlation id; used as idempotency key.");
        builder.HasIndex(cn => cn.CorrelationId)
            .IsUnique()
            .HasDatabaseName("UX_CreditNotes_CorrelationId");

        builder.Property(cn => cn.IssueDate)
            .HasComment("UTC timestamp when the credit note was issued (number stamped + PDF stored).");

        builder.Property(cn => cn.DeliveredAtUtc)
            .HasComment("UTC timestamp when the credit note transitioned to Delivered (nullable).");

        builder.Property(cn => cn.Reason)
            .HasComment("CreditNoteReason (v1: OrderCancelled).")
            .HasConversion(
                reason => reason.Value,
                value => CreditNoteReason.FromValue(value));

        builder.Property(cn => cn.Status)
            .HasComment("Credit-note lifecycle status (Issued|Delivered|Archived).")
            .HasConversion(
                status => status.Value,
                value => CreditNoteStatus.FromValue(value));

        // Total — owned Money. Amount can be negative (Invariant I-CN-2).
        builder.OwnsOne(cn => cn.Total, money => InvoiceConfiguration.ConfigureMoney(money, "total"));
        builder.Navigation(cn => cn.Total).IsRequired();

        // PdfBlobRef — owned, nullable until Issue(). BlobName is the canonical immutable
        // identifier; the URI is computed on demand by callers via IBlobStore.GetSasUrlAsync.
        builder.OwnsOne(cn => cn.PdfBlobRef, pdf =>
        {
            pdf.Property(p => p.BlobName)
                .HasColumnName("pdf_blob_name")
                .HasMaxLength(PdfBlobRef.BlobNameMaxLength)
                .IsRequired();

            pdf.Property(p => p.ContentHash)
                .HasColumnName("pdf_content_hash")
                .HasMaxLength(ContentHashLength)
                .IsFixedLength()
                .HasComment("SHA-256 of the PDF bytes, lowercase hex (64 chars).");
            pdf.Property(p => p.SizeBytes)
                .HasColumnName("pdf_size_bytes")
                .HasComment("PDF size in bytes (>0).");
        });

        builder.OwnsMany(cn => cn.Lines, lines =>
        {
            lines.ToTable("credit_note_lines", t => t.HasComment(
                "CreditNoteLine items — backward-looking corrections of the source invoice's lines."));
            lines.WithOwner().HasForeignKey("CreditNoteId");
            lines.HasKey("CreditNoteId", nameof(CreditNoteLine.LineNumber));

            InvoiceConfiguration.ConfigureCreditNoteLine(lines);
        });
        builder.Metadata
            .FindNavigation(nameof(CreditNote.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
