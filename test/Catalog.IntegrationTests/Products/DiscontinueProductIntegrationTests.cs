using Catalog.Application.Categories.CreateCategory;
using Catalog.Application.Products.CreateProduct;
using Catalog.Application.Products.DiscontinueProduct;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence.Database;
using Catalog.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;

namespace Catalog.IntegrationTests.Products;

/// <summary>
/// Integration coverage for DiscontinueProduct against the real
/// DispatchDomainEventsInterceptor + UpdateAuditableEntitiesInterceptor on a Postgres
/// Testcontainer: verifies the discontinue command persists the status transition and
/// stamps <c>LastModifiedUtc</c>. Tight time-source threading (handler vs. interceptor
/// pulling from the same <see cref="TimeProvider"/>) is owned by the M4.3 unit tests
/// (which inject a <see cref="FakeTimeProvider"/> directly per ADR-0015); this test
/// only asserts the persisted timestamp lands within a few seconds of wall-clock to
/// catch a regression where the audit column is left null or far-future.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class DiscontinueProductIntegrationTests : BaseIntegrationTest
{
    public DiscontinueProductIntegrationTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task DiscontinueProduct_PersistsStatusAndStampsLastModifiedUtc()
    {
        // Arrange — seed a category and an Active product through the real CreateProduct
        // pipeline (post-#177 products are Active on create — no separate Activate step).
        var run = Guid.CreateVersion7().ToString("N")[..8];
        Guid productId;
        Guid categoryId;
        using (var scope = Fixture.CreateScope())
        {
            var categoryHandler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<CreateCategoryCommand, Guid>>();
            categoryId = (await categoryHandler.HandleAsync(
                new CreateCategoryCommand { Name = $"DiscRoot-{run}" },
                TestContext.Current.CancellationToken)).Value;

            var productHandler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<CreateProductCommand, Guid>>();
            var sku = $"DISC-{run}".ToUpperInvariant();
            productId = (await productHandler.HandleAsync(
                new CreateProductCommand
                {
                    Sku = sku,
                    Name = "Discontinue Widget",
                    Description = "Round-trips through discontinue.",
                    CategoryId = categoryId,
                    Brand = "TestBrand",
                    Price = new MoneyDto { Amount = 1m, Currency = "USD" },
                    Images = [],
                },
                TestContext.Current.CancellationToken)).Value;
        }

        // Capture wall-clock snapshot just before Act so the BeCloseTo assert below has
        // a stable reference frame (production code resolves TimeProvider.System; a few
        // ms of jitter is tolerable).
        var expectedLastModifiedUtc = DateTimeOffset.UtcNow;

        // Act
        using (var scope = Fixture.CreateScope())
        {
            var handler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<DiscontinueProductCommand>>();
            var result = await handler.HandleAsync(
                new DiscontinueProductCommand
                {
                    ProductId = productId,
                    Reason = "End of life",
                },
                TestContext.Current.CancellationToken);
            result.Should().BeSuccess();
        }

        // Assert — UpdateAuditableEntitiesInterceptor stamps LastModifiedUtc from
        // TimeProvider.System; the M4.3 unit test layer asserts handler+interceptor
        // pull from the same TimeProvider instance.
        using var verifyScope = Fixture.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var persisted = await verifyDb.Products
            .AsNoTracking()
            .FirstAsync(p => p.Id == productId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            persisted.Status.Should().Be(Catalog.Domain.Products.ValueObjects.ProductStatus.Discontinued);
            persisted.LastModifiedUtc.Should().BeCloseTo(expectedLastModifiedUtc, TimeSpan.FromSeconds(5));
        }
    }
}
