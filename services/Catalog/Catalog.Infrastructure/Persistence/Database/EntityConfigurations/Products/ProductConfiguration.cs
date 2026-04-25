using Catalog.Domain.Products;
using Catalog.Domain.Products.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.SharedKernel.ValueObjects;

namespace Catalog.Infrastructure.Persistence.Database.EntityConfigurations.Products;

/// <summary>
/// EF Core mapping for the <see cref="Product"/> aggregate root. Mirrors the
/// <c>FakeCatalogDbContext</c> shape that drives the Catalog unit tests, while adding
/// Postgres-specific column types, comments, and indexes per <c>catalog.md § 9</c>.
/// </summary>
internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", t => t.HasComment(
            "Product aggregate — write-side state for a sellable item."));

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .ValueGeneratedNever()
            .HasComment("Primary key (Guid v7 — time-ordered).");

        // Optimistic concurrency via Postgres xmin system column (Ordering precedent).
        // RowVersion is inherited from Platform.SharedKernel.Base.Entity<TId>; mapping it as
        // a row-version property tells Npgsql to bind to the xmin system column (no stored column).
        builder.Property(p => p.RowVersion)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasComment("Optimistic concurrency token (Postgres xmin system column).");

        builder.Property(p => p.CategoryId)
            .HasComment("Catalog category identifier — referenced by id only, no navigation per arch test.");
        builder.HasIndex(p => p.CategoryId).HasDatabaseName("IX_Products_CategoryId");

        builder.Property(p => p.Status)
            .HasComment("Lifecycle status (Draft|Active|Discontinued); SmartEnum integer Value.")
            .HasConversion(
                status => status.Value,
                value => ProductStatus.FromValue(value));
        builder.HasIndex(p => p.Status).HasDatabaseName("IX_Products_Status");

        // Unique index on Sku.Value at the outer (Product table) level, mirroring Ordering's
        // index pattern. Defining it here avoids the owned-type index ambiguity that arises
        // when the index is declared inside the OwnsOne builder.
        builder.HasIndex(p => p.Sku.Value)
            .IsUnique()
            .HasDatabaseName("UX_Products_Sku");

        builder.Property(p => p.CreatedUtc)
            .HasComment("Row-level audit: created timestamp (UTC). Set by interceptor.");
        builder.Property(p => p.LastModifiedUtc)
            .HasComment("Row-level audit: last-modified timestamp (UTC). Set by interceptor.");

        builder.OwnsOne(p => p.Sku, sku =>
        {
            sku.Property(s => s.Value)
                .HasColumnName("sku")
                .HasMaxLength(Sku.MaxLength)
                .IsRequired()
                .HasComment("Business key — unique per Catalog.");
        });
        builder.Navigation(p => p.Sku).IsRequired();

        builder.OwnsOne(p => p.Name, name =>
        {
            name.Property(n => n.Value)
                .HasColumnName("name")
                .HasMaxLength(ProductName.MaxLength)
                .IsRequired()
                .HasComment("Display name (1..200).");
        });
        builder.Navigation(p => p.Name).IsRequired();

        builder.OwnsOne(p => p.Description, desc =>
        {
            desc.Property(d => d.Value)
                .HasColumnName("description")
                .HasColumnType("text")
                .IsRequired()
                .HasComment("Long-form description; truncated to 4000 chars at write time.");
        });
        builder.Navigation(p => p.Description).IsRequired();

        builder.OwnsOne(p => p.Brand, brand =>
        {
            brand.Property(b => b.Value)
                .HasColumnName("brand_name")
                .HasMaxLength(BrandName.MaxLength)
                .IsRequired()
                .HasComment("Brand name (1..100).");
        });
        builder.Navigation(p => p.Brand).IsRequired();

        builder.OwnsOne(p => p.Price, price =>
        {
            price.Property(m => m.Amount)
                .HasColumnName("price_amount")
                .HasPrecision(19, 4)
                .HasComment("Price amount.");
            price.Property(m => m.Currency)
                .HasColumnName("price_currency")
                .HasMaxLength(3)
                .HasComment("ISO 4217 currency code.")
                .HasConversion(
                    c => c.Name,
                    name => CurrencyCode.FromName(name, ignoreCase: false));
        });
        builder.Navigation(p => p.Price).IsRequired();

        builder.OwnsOne(p => p.Dimensions, dim =>
        {
            dim.Property(d => d.Length)
                .HasColumnName("dimensions_length")
                .HasPrecision(10, 2);
            dim.Property(d => d.Width)
                .HasColumnName("dimensions_width")
                .HasPrecision(10, 2);
            dim.Property(d => d.Height)
                .HasColumnName("dimensions_height")
                .HasPrecision(10, 2);
            dim.Property(d => d.Unit)
                .HasColumnName("dimensions_unit")
                .HasMaxLength(8);
        });
        builder.Navigation(p => p.Dimensions).IsRequired(false);

        builder.OwnsMany(p => p.Images, images =>
        {
            images.ToTable("product_images", t => t.HasComment(
                "Ordered image collection — owned by the Product aggregate."));
            images.WithOwner().HasForeignKey("ProductId");
            images.Property<int>("Ordinal");
            images.HasKey("ProductId", "Ordinal");

            images.Property(i => i.Url)
                .HasColumnName("url")
                .HasMaxLength(2048)
                .IsRequired();
            images.Property(i => i.AltText)
                .HasColumnName("alt_text")
                .HasMaxLength(ImageReference.MaxAltTextLength)
                .IsRequired();
            images.Property(i => i.DisplayOrder)
                .HasColumnName("display_order");
        });
        builder.Metadata
            .FindNavigation(nameof(Product.Images))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
