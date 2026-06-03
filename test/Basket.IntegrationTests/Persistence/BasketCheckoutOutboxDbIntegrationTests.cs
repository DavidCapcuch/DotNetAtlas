using Basket.Application.Baskets.Checkout;
using Basket.Application.Baskets.Common.Contracts;
using Basket.Domain.Baskets.Errors;
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
using Platform.Test.Framework.Assertions;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.IntegrationTests.Persistence;

/// <summary>
/// DB-backed twin of <c>BasketCheckoutOutboxIntegrationTests</c>. Where
/// the test stubs <c>ITransactionalOutbox</c> with NSubstitute, this
/// test exercises the full pipeline against a real <see cref="BasketDbContext"/>
/// running on a Postgres Testcontainer — proving:
/// <list type="bullet">
/// <item>The migration applies and creates <c>basket.outbox_messages</c>.</item>
/// <item>The Application layer's outbox publisher writes a row with the
/// correct topic + Kafka key + Avro type name.</item>
/// <item><see cref="BasketDbContext"/>'s <c>SaveChangesAsync</c> commits
/// the outbox row atomically with the rest of the unit-of-work.</item>
/// </list>
/// Avro byte-level fidelity is intentionally NOT asserted here — see
/// <see cref="FakeOutboxWriter"/> for the rationale (matches Inventory + Ordering
/// precedent of decoupling outbox tests from Schema Registry).
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class BasketCheckoutOutboxDbIntegrationTests : BaseIntegrationTest
{
    private readonly IntegrationTestFixture _fixture;

    public BasketCheckoutOutboxDbIntegrationTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CheckoutCommand_FullPipeline_PersistsOutboxRowToBasketSchema()
    {
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();

        var basket = BasketAggregate.Create(userId, IntegrationTestFixture.Now);
        basket.AddItem(productId, BuildSnapshot(amount: 19.9900m), 3, IntegrationTestFixture.Now);
        // Drain creation + add-item events so only Checkout's event flows.
        _ = basket.PopDomainEvents();

        _fixture.Repository
            .GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(basket));
        _fixture.Repository
            .SaveAsync(Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());
        _fixture.Repository
            .DeleteAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CheckoutBasketCommand, Guid>>();

        var result = await handler.HandleAsync(
            new CheckoutBasketCommand(
                userId,
                ValidAddress("US"),
                ValidAddress("CZ"),
                paymentMethodId),
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            // The handler pre-assigns the OrderId (UUID v7) — ADR-0029.
            result.Value.Should().NotBe(Guid.Empty);
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
            rows[0].Type.Should().BeMessageType<Basket.Sessions.BasketCheckoutInitiatedEvent>(
                "the CLR FullName of the Avro contract from Platform.SchemaRegistry.Contracts");
        }

        await _fixture.Repository.Received(1).DeleteAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TwoConcurrentCheckoutsForSameUser_PersistExactlyOneOutboxRow()
    {
        // C-1 regression guard, DB-backed twin of the former in-process pipeline test.
        // Two parallel checkouts for the same user both load the basket. Without the CAS-save
        // wrap in CheckoutBasketCommandHandler, each invocation would dispatch its domain event
        // AND commit an outbox row — two BasketCheckoutInitiatedEvent records on basket.sessions
        // for one user (a double-charge risk in the Checkout saga). With the fix, the CAS loser
        // surfaces BasketConcurrencyError (after one retry) and never reaches the outbox, so the
        // basket.outbox_messages table holds exactly one row. Asserting against real Postgres (not
        // a substituted outbox) is what this DB-backed test adds over the handler unit tests.
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();

        // Each load returns a FRESH aggregate — in production every concurrent request rehydrates
        // its own instance from Redis, so this avoids a shared-instance data race while preserving
        // the CAS semantics under test. The retry path reloads via GetByUserIdAsync, so a fresh
        // instance per call is also correct for the loser's single retry.
        BasketAggregate FreshBasket()
        {
            var b = BasketAggregate.Create(userId, IntegrationTestFixture.Now);
            b.AddItem(productId, BuildSnapshot(amount: 19.9900m), 3, IntegrationTestFixture.Now);
            _ = b.PopDomainEvents();
            return b;
        }

        _fixture.Repository
            .GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(_ => Result.Ok<BasketAggregate?>(FreshBasket()));
        _fixture.Repository
            .DeleteAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        // Simulate Redis CAS: exactly one SaveAsync wins; every later attempt sees the bumped
        // version and fails BasketConcurrencyError. Interlocked serialises across the two parallel
        // handlers (and the loser's one retry).
        var saveCount = 0;
        _fixture.Repository
            .SaveAsync(Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref saveCount) == 1
                ? Result.Ok()
                : Result.Fail(new BasketConcurrencyError(userId, expected: 1, actual: 2)));

        using var scope1 = _fixture.CreateScope();
        using var scope2 = _fixture.CreateScope();
        var handler1 = scope1.ServiceProvider
            .GetRequiredService<ICommandHandler<CheckoutBasketCommand, Guid>>();
        var handler2 = scope2.ServiceProvider
            .GetRequiredService<ICommandHandler<CheckoutBasketCommand, Guid>>();

        var task1 = handler1.HandleAsync(MakeCommand(userId), TestContext.Current.CancellationToken);
        var task2 = handler2.HandleAsync(MakeCommand(userId), TestContext.Current.CancellationToken);
        var results = await Task.WhenAll(task1, task2);

        using (new AssertionScope())
        {
            results.Count(r => r.IsSuccess).Should().Be(1, "exactly one checkout wins the CAS race");
            results.Count(r => r.IsFailed).Should().Be(1, "the loser surfaces BasketConcurrencyError after one retry");
        }

        // Authoritative assertion: real Postgres holds exactly one outbox row for this user.
        using var verifyScope = _fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<BasketDbContext>();
        var rows = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == userId.ToString())
            .ToListAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            rows.Should().ContainSingle("the CAS loser must never reach the outbox");
            rows[0].TopicName.Should().Be("basket.sessions");
            rows[0].Type.Should().BeMessageType<Basket.Sessions.BasketCheckoutInitiatedEvent>();
        }
    }

    private static CheckoutBasketCommand MakeCommand(Guid userId) => new(
        userId,
        ValidAddress("US"),
        ValidAddress("CZ"),
        Guid.CreateVersion7());

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
            Money.Create(amount, CurrencyCode.Usd).Value,
            new DateTimeOffset(2026, 01, 15, 09, 30, 00, TimeSpan.Zero));
}
