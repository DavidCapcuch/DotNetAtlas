using Catalog.Application.Common.Data;
using Catalog.Application.Common.ReadModels;
using Catalog.Domain.Categories;
using Catalog.Domain.Categories.ValueObjects;
using Catalog.Domain.Products;
using Catalog.Domain.Products.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.SharedKernel.ValueObjects;

namespace Catalog.UnitTests.Common;

/// <summary>
/// In-memory EF Core implementation of <see cref="ICatalogDbContext"/> used by unit tests.
/// Configures VOs via <c>OwnsOne</c> so InMemory can persist <see cref="Product"/> and
/// <see cref="Category"/> aggregates faithfully enough for handler / projection / outbox tests.
/// Transactional semantics, raw SQL, and JSON columns are NOT emulated — that's integration
/// territory with a real Postgres Testcontainers fixture.
/// </summary>
public class FakeCatalogDbContext : DbContext, ICatalogDbContext
{
    public FakeCatalogDbContext(DbContextOptions<FakeCatalogDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<ProductSearchViewRow> ProductSearchView => Set<ProductSearchViewRow>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public static FakeCatalogDbContext Create(string? databaseName = null, params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<FakeCatalogDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.CreateVersion7().ToString())
            // The InMemory provider has no transactions; the production CatalogDbContext on
            // Postgres uses Database.EnsureTransactionAsync (e.g. ReparentCategoryCommandHandler).
            // Tell the provider to silently ignore the transaction so unit tests still exercise
            // the same handler code path; the SQL-level transactional semantics are covered by
            // Catalog.IntegrationTests against a Postgres Testcontainer.
            .ConfigureWarnings(b => b.Ignore(InMemoryEventId.TransactionIgnoredWarning));

        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return new FakeCatalogDbContext(builder.Options);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Product>(b =>
        {
            b.HasKey(p => p.Id);
            b.Ignore(nameof(Platform.SharedKernel.Base.AggregateRoot<Guid>));
            b.OwnsOne(p => p.Sku);
            b.OwnsOne(p => p.Name);
            b.OwnsOne(p => p.Description);
            b.OwnsOne(p => p.Brand);
            b.OwnsOne(p => p.Price, price =>
            {
                price.Property(m => m.Amount);
                price.Property(m => m.Currency).HasConversion<string>(
                    v => v.Name,
                    v => CurrencyCode.FromName(v, ignoreCase: false));
            });
            b.OwnsOne(p => p.Dimensions);
            b.OwnsMany(p => p.Images, img =>
            {
                img.WithOwner().HasForeignKey("ProductId");
                img.Property<int>("Id");
                img.HasKey("ProductId", "Id");
            });
            b.Property(p => p.Status).HasConversion(
                v => v.Value,
                v => ProductStatus.FromValue(v));
        });

        modelBuilder.Entity<Category>(b =>
        {
            b.HasKey(c => c.Id);
            b.OwnsOne(c => c.Path);
        });

        modelBuilder.Entity<ProductSearchViewRow>(b =>
        {
            b.HasKey(r => r.ProductId);
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.HasKey(m => m.Id);
        });
    }
}
