using Catalog.Application.Products.DiscontinueProduct;
using Catalog.Domain.Products.Events;
using Catalog.Infrastructure.Persistence.Database.Interceptors;
using Catalog.UnitTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Catalog.UnitTests.Products.DiscontinueProduct;

/// <summary>
/// Verifies that <see cref="DiscontinueProductCommandHandler"/> and
/// <see cref="UpdateAuditableEntitiesInterceptor"/> read from the same
/// <see cref="TimeProvider"/> instance — the production DI graph registers
/// <c>TimeProvider</c> as a singleton (Generic Host default), so the
/// handler-stamped <see cref="ProductDiscontinuedDomainEvent.OccurredOnUtc"/>
/// and the interceptor-stamped <see cref="Catalog.Domain.Products.Product.LastModifiedUtc"/>
/// MUST coincide. A regression where DI accidentally gives them separate
/// clocks (e.g. one resolving <c>TimeProvider.System</c> and another resolving
/// a stale fake) would silently desynchronise audit trails and emitted events.
/// </summary>
/// <remarks>
/// Restores coverage that was previously embedded in
/// <c>Catalog.IntegrationTests.Products.DiscontinueProductIntegrationTests</c>
/// before ADR-0015's "tests construct FakeTimeProvider locally" pattern made
/// the integration-level frozen-clock assertion non-trivial. This unit-of-collaboration
/// test holds the property at the cheapest tier.
/// </remarks>
public class DiscontinueProductClockSourceTests
{
    [Fact]
    public async Task Handler_and_interceptor_stamp_the_same_instant_when_sharing_one_TimeProvider()
    {
        var pinnedNow = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(pinnedNow);

        await using var db = FakeCatalogDbContext.Create(
            databaseName: null,
            new UpdateAuditableEntitiesInterceptor(clock));

        var category = CatalogFactories.RootCategory(utcNow: pinnedNow.AddDays(-1));
        db.Categories.Add(category);
        var product = CatalogFactories.ActiveProduct(category, utcNow: pinnedNow.AddDays(-1));
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new DiscontinueProductCommandHandler(
            db, clock, NullLogger<DiscontinueProductCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new DiscontinueProductCommand { ProductId = product.Id, Reason = "EOL" },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();

            var refreshed = await db.Products.FirstAsync(
                p => p.Id == product.Id, TestContext.Current.CancellationToken);

            // Interceptor stamp — production wires UpdateAuditableEntitiesInterceptor against
            // the same TimeProvider singleton the handler resolves; with the shared clock here
            // the stamp MUST equal the pinned instant.
            refreshed.LastModifiedUtc.Should().Be(pinnedNow);

            // Handler stamp — Product.Discontinue receives utcNow from the handler's TimeProvider
            // and embeds it as the domain event's OccurredOnUtc. Equal-to-interceptor proves they
            // observed the same clock instance.
            var domainEvent = refreshed.PopDomainEvents()
                .OfType<ProductDiscontinuedDomainEvent>()
                .Should().ContainSingle().Subject;
            domainEvent.OccurredOnUtc.Should().Be(pinnedNow);
            domainEvent.OccurredOnUtc.Should().Be(refreshed.LastModifiedUtc);
        }
    }

    [Fact]
    public async Task Handler_and_interceptor_disagree_when_each_holds_a_different_TimeProvider()
    {
        // Negative case: if a regression splits the TimeProvider singleton into two distinct
        // instances at separate instants, OccurredOnUtc and LastModifiedUtc diverge. This guards
        // the assertion above from passing trivially (e.g. if both fields silently used
        // DateTimeOffset.UtcNow regardless of the injected TimeProvider).
        var handlerNow = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var interceptorNow = handlerNow.AddMinutes(5);
        var handlerClock = new FakeTimeProvider(handlerNow);
        var interceptorClock = new FakeTimeProvider(interceptorNow);

        await using var db = FakeCatalogDbContext.Create(
            databaseName: null,
            new UpdateAuditableEntitiesInterceptor(interceptorClock));

        var category = CatalogFactories.RootCategory(utcNow: handlerNow.AddDays(-1));
        db.Categories.Add(category);
        var product = CatalogFactories.ActiveProduct(category, utcNow: handlerNow.AddDays(-1));
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new DiscontinueProductCommandHandler(
            db, handlerClock, NullLogger<DiscontinueProductCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new DiscontinueProductCommand { ProductId = product.Id, Reason = "EOL" },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            var refreshed = await db.Products.FirstAsync(
                p => p.Id == product.Id, TestContext.Current.CancellationToken);

            refreshed.LastModifiedUtc.Should().Be(interceptorNow);
            var domainEvent = refreshed.PopDomainEvents()
                .OfType<ProductDiscontinuedDomainEvent>()
                .Should().ContainSingle().Subject;
            domainEvent.OccurredOnUtc.Should().Be(handlerNow);
            domainEvent.OccurredOnUtc.Should().NotBe(refreshed.LastModifiedUtc);
        }
    }
}
