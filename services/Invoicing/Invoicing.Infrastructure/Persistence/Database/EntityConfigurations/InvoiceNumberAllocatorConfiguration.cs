using Invoicing.Application.Common.Numbering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Invoicing.Infrastructure.Persistence.Database.EntityConfigurations;

/// <summary>
/// EF mapping for <see cref="InvoiceNumberAllocator"/>. One row per fiscal
/// year. The <c>FOR UPDATE</c> row lock that <c>PostgresInvoiceNumberAllocator</c>
/// holds during issuance only works because <see cref="InvoiceNumberAllocator.Year"/>
/// is the primary key — Postgres takes the lock on the indexed PK row.
/// </summary>
internal sealed class InvoiceNumberAllocatorConfiguration : IEntityTypeConfiguration<InvoiceNumberAllocator>
{
    public void Configure(EntityTypeBuilder<InvoiceNumberAllocator> builder)
    {
        builder.ToTable("invoice_number_allocator", t =>
        {
            t.HasComment(
                "Gap-free invoice-number allocator (ADR-0018). One row per fiscal year. "
                + "Locked with SELECT ... FOR UPDATE inside the issuing transaction; "
                + "rollback releases the lock without incrementing next_value.");
            t.HasCheckConstraint("ck_invoice_number_allocator_next_value", "next_value >= 1");
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
