using Catalog.Application.Common.ReadModels;
using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Platform.ReliableMessaging.Outbox.EFCore;

namespace Catalog.Application.Common.Data;

/// <summary>
/// Application-owned contract for the Catalog DbContext. Exposed to handlers so the Application
/// layer does not depend on a concrete EF Core context in <c>Catalog.Infrastructure</c>.
/// Extends <see cref="IOutboxDbContext"/> so transactional-outbox writes flow through the same
/// <c>SaveChangesAsync</c> call as aggregate + projection writes (CQRS in one transaction).
/// </summary>
public interface ICatalogDbContext : IOutboxDbContext
{
    /// <summary>Write-model set for Product aggregates.</summary>
    DbSet<Product> Products { get; }

    /// <summary>Write-model set for Category aggregates.</summary>
    DbSet<Category> Categories { get; }

    /// <summary>
    /// Denormalized read-side projection, upserted in-process by domain-event handlers
    /// atomically with the write-model (ProductSearchView pattern).
    /// </summary>
    DbSet<ProductSearchViewRow> ProductSearchView { get; }
}
