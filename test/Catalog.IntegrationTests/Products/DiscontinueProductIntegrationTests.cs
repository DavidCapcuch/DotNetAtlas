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
/// Integration coverage for DiscontinueProduct's deterministic-time threading per wave-1
/// follow-up #194. M4.3 unit tests pin the threading by mocking TimeProvider; this test
/// verifies the same end-to-end through the real DispatchDomainEventsInterceptor +
/// UpdateAuditableEntitiesInterceptor against a Postgres Testcontainer, so a regression
/// where the handler reaches for DateTimeOffset.UtcNow (ADR-0015) surfaces here.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DiscontinueProductIntegrationTests
{
    private readonly IntegrationTestFixture _fixture;

    public DiscontinueProductIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DiscontinueProduct_PersistsLastModifiedUtcAtTheFakeClock()
    {
        // Arrange — seed a Draft product through the real CreateProduct pipeline, then
        // promote it to Active via the domain method directly (ActivateProductCommand is a
        // contract extension tracked in #177 and not present in v1).
        var run = Guid.CreateVersion7().ToString("N")[..8];
        Guid productId;
        Guid categoryId;
        using (var scope = _fixture.CreateScope())
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

        // Post-#177: products are Active on create — no separate Activate step. Move the
        // clock forward by 1 hour so the Discontinue write has a distinguishable
        // LastModifiedUtc from the Create write.
        _fixture.TimeProvider.Advance(TimeSpan.FromHours(1));
        var expectedLastModifiedUtc = _fixture.TimeProvider.GetUtcNow();

        // Act
        using (var scope = _fixture.CreateScope())
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

        // Assert — UpdateAuditableEntitiesInterceptor stamps LastModifiedUtc from the
        // injected TimeProvider; the handler's own _timeProvider.GetUtcNow() feeds the
        // ProductDiscontinuedDomainEvent. If anyone reaches for static DateTimeOffset.UtcNow
        // by accident, LastModifiedUtc would diverge from the FakeTimeProvider value.
        using var verifyScope = _fixture.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var persisted = await verifyDb.Products
            .AsNoTracking()
            .FirstAsync(p => p.Id == productId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            persisted.Status.Should().Be(Catalog.Domain.Products.ValueObjects.ProductStatus.Discontinued);
            persisted.LastModifiedUtc.Should().Be(expectedLastModifiedUtc);
        }
    }
}
