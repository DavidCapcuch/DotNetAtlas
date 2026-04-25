using Catalog.Domain.Categories;
using Catalog.Domain.Categories.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence.Database.EntityConfigurations.Categories;

/// <summary>
/// EF Core mapping for the <see cref="Category"/> aggregate root. Stores the materialized
/// <c>Path</c> column flat (single string) to make the prefix-based descendant queries from
/// <c>CategoryPathService</c> efficient on Postgres.
/// </summary>
internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories", t => t.HasComment(
            "Category aggregate — taxonomy node with materialized path."));

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .ValueGeneratedNever()
            .HasComment("Primary key (Guid v7 — time-ordered).");

        // Optimistic concurrency via Postgres xmin system column (Ordering precedent).
        builder.Property(c => c.RowVersion)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasComment("Optimistic concurrency token (Postgres xmin system column).");

        builder.Property(c => c.Name)
            .HasMaxLength(Category.MaxNameLength)
            .IsRequired()
            .HasComment("Display name (1..100).");

        builder.Property(c => c.ParentCategoryId)
            .IsRequired(false)
            .HasComment("Parent category id (null for roots) — referenced by id only, no navigation.");

        builder.Property(c => c.CreatedUtc)
            .HasComment("Row-level audit: created timestamp (UTC). Set by interceptor.");
        builder.Property(c => c.LastModifiedUtc)
            .HasComment("Row-level audit: last-modified timestamp (UTC). Set by interceptor.");

        builder.OwnsOne(c => c.Path, path =>
        {
            path.Property(p => p.Value)
                .HasColumnName("path")
                .HasMaxLength(256)
                .IsRequired()
                .HasComment("Materialized closure (max depth 5).");
            path.HasIndex(p => p.Value).HasDatabaseName("IX_Categories_Path");
        });
        builder.Navigation(c => c.Path).IsRequired();
    }
}
