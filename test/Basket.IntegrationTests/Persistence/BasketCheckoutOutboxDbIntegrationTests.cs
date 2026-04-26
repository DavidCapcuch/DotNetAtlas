using Basket.Application.Baskets.Checkout;
using Basket.Application.Baskets.Common.Contracts;
using Basket.Domain.Baskets.ValueObjects;
using Basket.Infrastructure.Persistence.Database;
using Basket.IntegrationTests.Common;
using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Platform.CQRS;
using Platform.SharedKernel.ValueObjects;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.IntegrationTests.Persistence;

/// <summary>
/// DB-backed twin of <c>BasketCheckoutOutboxIntegrationTests</c> (M4). Where
/// the M4 test stubs <c>ITransactionalOutbox</c> with NSubstitute, this M6
/// test exercises the full pipeline against a real <see cref="BasketDbContext"/>
/// running on a Postgres Testcontainer — proving:
/// <list type="bullet">
///   <item>The migration applies and creates <c>basket.OutboxMessages</c>.</item>
///   <item>The Application layer's outbox publisher writes a row with the
///         correct topic + Kafka key + Avro type name.</item>
///   <item><see cref="BasketDbContext"/>'s <c>SaveChangesAsync</c> commits
///         the outbox row atomically with the rest of the unit-of-work.</item>
/// </list>
/// Avro byte-level fidelity is intentionally NOT asserted here — see
/// <see cref="FakeOutboxWriter"/> for the rationale (matches Inventory + Ordering
/// precedent of decoupling outbox tests from Schema Registry).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class BasketCheckoutOutboxDbIntegrationTests
{
    private readonly IntegrationTestFixture _fixture;

    public BasketCheckoutOutboxDbIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CheckoutCommand_FullPipeline_PersistsOutboxRowToBasketSchema()
    {
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();

        var basket = BasketAggregate.Create(userId, IntegrationTestFixture.Now);
        basket.AddItem(productId, BuildSnapshot(amount: 19.9900m), 3, IntegrationTestFixture.Now);
        // Drain creation + add-item events so only Checkout's event flows.
        _ = basket.PopDomainEvents();

        _fixture.Repository
            .GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(basket));
        _fixture.Repository
            .DeleteAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CheckoutBasketCommand, Guid>>();

        var result = await handler.HandleAsync(
            new CheckoutBasketCommand(
                userId,
                correlationId,
                ValidAddress("US"),
                ValidAddress("CZ"),
                paymentMethodId),
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Should().Be(correlationId);
        }

        // Re-resolve the DbContext from a fresh scope to bypass the EF
        // first-level cache and read what was actually committed to Postgres.
        using var verifyScope = _fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<BasketDbContext>();

        var rows = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == userId.ToString())
            .ToListAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            rows.Should().HaveCount(1, "exactly one BasketCheckoutInitiatedEvent should be produced per checkout");
            rows[0].TopicName.Should().Be("basket.sessions",
                "ADR-0007 + events-catalog.md § 5.2 lock the topic name");
            rows[0].KafkaKey.Should().Be(userId.ToString(),
                "Kafka key partitions on user so a single user's events stay ordered");
            rows[0].Type.Should().Be("Basket.Sessions.BasketCheckoutInitiatedEvent",
                "the CLR FullName of the Avro contract from Platform.SchemaRegistry.Contracts");
        }

        await _fixture.Repository.Received(1).DeleteAsync(userId, Arg.Any<CancellationToken>());
    }

    private static CheckoutAddressDto ValidAddress(string countryCode) => new()
    {
        Street1 = "1 Main St",
        City = "Springfield",
        PostalCode = "62704",
        CountryCode = countryCode,
    };

    private static ProductSnapshot BuildSnapshot(decimal amount) =>
        ProductSnapshot.Create(
            "SKU-1",
            "Product 1",
            new Money(amount, CurrencyCode.Usd),
            new DateTimeOffset(2026, 01, 15, 09, 30, 00, TimeSpan.Zero));
}
