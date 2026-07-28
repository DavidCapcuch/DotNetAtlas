using Catalog.Application.Common.ReadModels;
using Catalog.Domain.Categories;
using Catalog.Domain.Products.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence.Database.EntityConfigurations.ReadModels;

/// <summary>
/// EF Core mapping for the <c>product_search_view</c> denormalized read model.
/// Per <c>catalog.md § 9</c>, this row is upserted in the same DbContext transaction as
/// the write-model save by the <c>ProductSearchView*ProjectionHandler</c> set.
/// </summary>
internal sealed class ProductSearchViewRowConfiguration : IEntityTypeConfiguration<ProductSearchViewRow>
{
    public void Configure(EntityTypeBuilder<ProductSearchViewRow> builder)
    {
        builder.ToTable("product_search_view", t =>
        {
            t.HasComment(
                "Denormalized read view of Product (catalog.md § 9). "
                + "Upserted in-process by domain-event handlers in the same transaction as write-model saves.");

            // Dimensions is one optional value object flattened across four columns, so a partial row
            // is meaningless. Structural before the flattening (one JSONB column, present or absent);
            // this keeps it structural instead of leaving it to the writer's discipline.
            t.HasCheckConstraint(
                "ck_product_search_view_dimensions_all_or_none",
                "num_nonnulls(dimensions_length, dimensions_width, dimensions_height, dimensions_unit) IN (0, 4)");
        });

        builder.HasKey(r => r.ProductId);
        builder.Property(r => r.ProductId)
            .ValueGeneratedNever()
            .HasComment("Mirrors Product.Id.");

        builder.Property(r => r.Sku)
            .HasMaxLength(Sku.MaxLength)
            .IsRequired();
        builder.HasIndex(r => r.Sku).IsUnique().HasDatabaseName("ux_product_search_view_sku");

        builder.Property(r => r.Name)
            .HasMaxLength(ProductName.MaxLength)
            .IsRequired();

        builder.Property(r => r.Description)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(r => r.CategoryId)
            .HasComment("Mirrors Product.CategoryId.");

        // FK to catalog.categories per catalog.md § 9. HasOne<Category> with no navigation keeps
        // the read row a flat POCO (no Category nav property), so the projection writes stay
        // simple while the database still enforces referential integrity.
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(r => r.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.CategoryId).HasDatabaseName("ix_product_search_view_category_id");

        builder.Property(r => r.CategoryPath)
            .HasMaxLength(256)
            .IsRequired()
            .HasComment("Materialized category path; rewritten on Reparent by CategoryPathService.");
        builder.HasIndex(r => r.CategoryPath).HasDatabaseName("ix_product_search_view_category_path");

        builder.Property(r => r.CategoryBreadcrumb)
            .HasMaxLength(512)
            .IsRequired()
            .HasComment("Denormalized human-readable breadcrumb — may temporarily lag after Reparent.");

        builder.Property(r => r.BrandName)
            .HasMaxLength(BrandName.MaxLength)
            .IsRequired();

        builder.Property(r => r.PriceAmount)
            .HasPrecision(19, 4);
        builder.HasIndex(r => r.PriceAmount).HasDatabaseName("ix_product_search_view_price_amount");

        builder.Property(r => r.PriceCurrency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(r => r.Status)
            .HasMaxLength(32)
            .IsRequired()
            .HasComment("Lifecycle status name (Active|Discontinued).");
        builder.HasIndex(r => r.Status).HasDatabaseName("ix_product_search_view_status");

        // Mirrors the write model's owned-type mapping on catalog.products column-for-column, so the
        // projection stores dimensions the same way the aggregate does.
        const string DimensionsComment =
            "Dimensions VO, flattened; all four are set together or all four are null (digital/service products).";
        builder.Property(r => r.DimensionsLength)
            .HasPrecision(10, 2)
            .HasComment(DimensionsComment);
        builder.Property(r => r.DimensionsWidth)
            .HasPrecision(10, 2)
            .HasComment(DimensionsComment);
        builder.Property(r => r.DimensionsHeight)
            .HasPrecision(10, 2)
            .HasComment(DimensionsComment);
        builder.Property(r => r.DimensionsUnit)
            .HasMaxLength(8)
            .HasComment(DimensionsComment);

        builder.Property(r => r.ImagesJson)
            .HasColumnType("jsonb")
            .HasDefaultValue("[]")
            .IsRequired();

        builder.Property(r => r.IsSellable)
            .HasDefaultValue(false)
            .HasComment("Computed flag — wired up by the StockLevelChangedEvent Kafka inbox consumer.");

        builder.Property(r => r.CreatedAtUtc);
        builder.Property(r => r.LastUpdatedAtUtc);
    }
}
