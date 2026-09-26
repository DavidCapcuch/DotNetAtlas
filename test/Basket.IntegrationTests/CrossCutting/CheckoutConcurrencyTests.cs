using Basket.Application.Abstractions;
using Basket.Application.Baskets.Checkout;
using Basket.Application.Baskets.Common.Contracts;
using Basket.Domain.Baskets.Errors;
using Basket.Domain.Baskets.ValueObjects;
using Basket.IntegrationTests.Common;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.ValueObjects;
using Platform.Test.Framework.Assertions;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.IntegrationTests.CrossCutting;

/// <summary>
/// The CAS-race half of checkout, which has no outer entrance: two concurrent HTTP checkouts
/// cannot be made to lose the race deterministically — they may simply serialize, and the second
/// then finds a deleted basket (404) instead of a bumped version. So the SUT is constructed
/// directly with a substituted <see cref="IBasketRepository"/> that programs the CAS outcome,
/// while everything the assertion depends on stays real: the scoped
/// <see cref="ITransactionalOutbox{TContext}"/>, the domain-event dispatcher, and Postgres itself.
/// The happy path is owned by the HTTP slice test in <c>ApiEndpoints/Baskets/CheckoutBasketTests</c>.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class CheckoutConcurrencyTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 01, 15, 09, 30, 00, TimeSpan.Zero);

    public CheckoutConcurrencyTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    [Trait("Category", "concurrency")]
    [Trait("Category", "regression")]
    public async Task TwoConcurrentCheckoutsForSameUser_PersistExactlyOneOutboxRow()
    {
        // Guards the CAS save in CheckoutBasketCommandHandler: without it both racers dispatch
        // the event and commit an outbox row — two BasketCheckoutInitiatedEvent on basket.sessions
        // for one user, a double charge in the Checkout saga.

        // Arrange
        var userId = Guid.CreateVersion7();
        var repository = RepositoryHoldingBasketFor(userId);

        // Simulate Redis CAS: the first SaveAsync wins; every later one — the other checkout and
        // its single retry — sees the bumped version and fails BasketConcurrencyError.
        var saveCount = 0;
        repository
            .SaveAsync(Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref saveCount) == 1
                ? Result.Ok()
                : Result.Fail(new BasketConcurrencyError(userId, expected: 1, actual: 2)));

        // Separate scopes, as two requests would have. The loser fails its CAS before touching the
        // database, so only the winner's scope commits.
        using var scope1 = Fixture.Services.CreateScope();
        using var scope2 = Fixture.Services.CreateScope();
        var handler1 = ActivatorUtilities.CreateInstance<CheckoutBasketCommandHandler>(scope1.ServiceProvider, repository);
        var handler2 = ActivatorUtilities.CreateInstance<CheckoutBasketCommandHandler>(scope2.ServiceProvider, repository);

        // Act
        var task1 = handler1.HandleAsync(MakeCommand(userId), TestContext.Current.CancellationToken);
        var task2 = handler2.HandleAsync(MakeCommand(userId), TestContext.Current.CancellationToken);
        var results = await Task.WhenAll(task1, task2);

        // Assert
        using (new AssertionScope())
        {
            results.Count(r => r.IsSuccess).Should().Be(1, "exactly one checkout wins the CAS race");
            results.Single(r => r.IsFailed).HasError<BasketConcurrencyError>().Should()
                .BeTrue("the loser surfaces BasketConcurrencyError after one retry");
        }

        // Authoritative assertion: real Postgres holds exactly one outbox row for this user.
        var rows = await ReadOutboxRowsAsync(userId);

        using (new AssertionScope())
        {
            rows.Should().ContainSingle("the CAS loser must never reach the outbox");
            rows[0].TopicName.Should().Be("basket.sessions");
            rows[0].Type.Should().BeMessageType<Basket.Sessions.BasketCheckoutInitiatedEvent>();
        }
    }

    [Fact]
    [Trait("Category", "concurrency")]
    [Trait("Category", "regression")]
    public async Task Checkout_WhenFirstSaveLosesCasRace_RetriesAndPersistsExactlyOneOutboxRow()
    {
        // The attempt that lost the CAS must leave nothing behind: its event is dispatched only
        // after its save succeeds, so the retry's commit carries the retry's outbox row alone.

        // Arrange
        var userId = Guid.CreateVersion7();
        var repository = RepositoryHoldingBasketFor(userId);
        repository
            .SaveAsync(Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail(new BasketConcurrencyError(userId, expected: 1, actual: 2)), Result.Ok());

        var handler = ActivatorUtilities.CreateInstance<CheckoutBasketCommandHandler>(Scope.ServiceProvider, repository);

        // Act
        var result = await handler.HandleAsync(MakeCommand(userId), TestContext.Current.CancellationToken);

        // Assert
        var rows = await ReadOutboxRowsAsync(userId);

        using (new AssertionScope())
        {
            result.IsSuccess.Should().BeTrue("the single retry wins the CAS");
            rows.Should().ContainSingle("the losing attempt must not leave an outbox row behind");
        }
    }

    /// <summary>
    /// A repository substitute where every load rehydrates a FRESH aggregate — in production each
    /// request (and each retry) reads its own instance from Redis, so sharing one would be a data
    /// race the real system never has. Callers program <c>SaveAsync</c> to decide the CAS outcome.
    /// </summary>
    private static IBasketRepository RepositoryHoldingBasketFor(Guid userId)
    {
        var productId = Guid.CreateVersion7();

        BasketAggregate FreshBasket()
        {
            var basket = BasketAggregate.Create(userId, CapturedAt);
            basket.AddItem(productId, BuildSnapshot(amount: 19.9900m), 3, CapturedAt);
            _ = basket.PopDomainEvents();
            return basket;
        }

        var repository = Substitute.For<IBasketRepository>();
        repository
            .GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(_ => Result.Ok<BasketAggregate?>(FreshBasket()));
        repository
            .DeleteAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok());
        return repository;
    }

    private Task<List<OutboxMessage>> ReadOutboxRowsAsync(Guid userId) =>
        DbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == userId.ToString())
            .ToListAsync(TestContext.Current.CancellationToken);

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
            CapturedAt);
}
