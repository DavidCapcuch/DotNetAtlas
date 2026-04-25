using Invoicing.Application.Common.Numbering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Invoicing.Infrastructure.Persistence.Database.EntityConfigurations;

/// <summary>
/// EF mapping for <see cref="CreditNoteNumberAllocator"/>. Symmetric to
/// <see cref="InvoiceNumberAllocatorConfiguration"/> but maps a separate
/// table so credit-note and invoice sequences advance independently per
/// ADR-0018.
/// </summary>
internal sealed class CreditNoteNumberAllocatorConfiguration : IEntityTypeConfiguration<CreditNoteNumberAllocator>
{
    public void Configure(EntityTypeBuilder<CreditNoteNumberAllocator> builder)
    {
        builder.ToTable("credit_note_number_allocator", t =>
        {
            t.HasComment(
                "Gap-free credit-note-number allocator (ADR-0018). One row per fiscal year. "
                + "Locked with SELECT ... FOR UPDATE inside the issuing transaction.");
            t.HasCheckConstraint("ck_credit_note_number_allocator_next_value", "next_value >= 1");
        });

        builder.HasKey(r => r.Year);

        builder.Property(r => r.Year)
            .HasColumnType("smallint")
            .ValueGeneratedNever()
            .HasComment("Fiscal year (e.g. 2026). Primary key.");

        builder.Property(r => r.NextValue)
            .HasColumnType("bigint")
            .IsRequired()
            .HasComment("Next sequence value to hand out for this year; first issuance starts at 1.");

        builder.Property(r => r.UpdatedAt)
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd()
            .HasComment("Refreshed on every increment via the allocator adapter.");
    }
}
