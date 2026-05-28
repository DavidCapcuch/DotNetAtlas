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
        builder.ToTable("product_search_view", t => t.HasComment(
            "Denormalized read view of Product (catalog.md § 9). "
            + "Upserted in-process by domain-event handlers in the same transaction as write-model saves."));

        builder.HasKey(r => r.ProductId);
        builder.Property(r => r.ProductId)
            .ValueGeneratedNever()
            .HasComment("Mirrors Product.Id.");

        builder.Property(r => r.Sku)
            .HasMaxLength(Sku.MaxLength)
            .IsRequired();
        builder.HasIndex(r => r.Sku).IsUnique().HasDatabaseName("UX_ProductSearchView_Sku");

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
        builder.HasIndex(r => r.CategoryId).HasDatabaseName("IX_ProductSearchView_CategoryId");

        builder.Property(r => r.CategoryPath)
            .HasMaxLength(256)
            .IsRequired()
            .HasComment("Materialized category path; rewritten on Reparent by CategoryPathService.");
        builder.HasIndex(r => r.CategoryPath).HasDatabaseName("IX_ProductSearchView_CategoryPath");

        builder.Property(r => r.CategoryBreadcrumb)
            .HasMaxLength(512)
            .IsRequired()
            .HasComment("Denormalized human-readable breadcrumb — may temporarily lag after Reparent.");

        builder.Property(r => r.BrandName)
            .HasMaxLength(BrandName.MaxLength)
            .IsRequired();

        builder.Property(r => r.PriceAmount)
            .HasPrecision(19, 4);
        builder.HasIndex(r => r.PriceAmount).HasDatabaseName("IX_ProductSearchView_PriceAmount");

        builder.Property(r => r.PriceCurrency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(r => r.Status)
            .HasMaxLength(32)
            .IsRequired()
            .HasComment("Lifecycle status name (Draft|Active|Discontinued).");
        builder.HasIndex(r => r.Status).HasDatabaseName("IX_ProductSearchView_Status");

        builder.Property(r => r.DimensionsJson)
            .HasColumnType("jsonb")
            .IsRequired(false)
            .HasComment("Serialized Dimensions VO; null for digital/service products.");

        builder.Property(r => r.ImagesJson)
            .HasColumnType("jsonb")
            .HasDefaultValue("[]")
            .IsRequired();

        builder.Property(r => r.IsSellable)
            .HasDefaultValue(false)
            .HasComment("Computed flag — wired up by the StockLevelChanged Kafka inbox consumer.");

        builder.Property(r => r.CreatedAtUtc);
        builder.Property(r => r.LastUpdatedAtUtc);

        builder.Property(r => r.CorrelationId)
            .HasComment(
                "Originating HTTP correlation id (ADR-0008). Populated from "
                + "HttpContext.Items[CorrelationIdContextKeys.HttpContextItemsKey] by the API layer, "
                + "or Guid.Empty when no HTTP pipeline is in play.")
            .HasDefaultValue(Guid.Empty);
    }
}
