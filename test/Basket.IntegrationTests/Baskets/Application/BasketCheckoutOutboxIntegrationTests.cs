using Basket.Application.Abstractions;
using Basket.Application.Baskets.Checkout;
using Basket.Application.Baskets.Common.Contracts;
using Basket.Application.Common;
using Basket.Application.Common.Data;
using Basket.Application.Common.Messaging;
using Basket.Domain.Baskets.Errors;
using Basket.Domain.Baskets.ValueObjects;
using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.EFCore;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.IntegrationTests.Baskets.Application;

/// <summary>
/// Outbox integration test for M4. Exercises the whole in-process pipeline —
/// <see cref="CheckoutBasketCommand"/> dispatched through the real
/// <c>AddApplication()</c> DI container (validation + tracing + logging + metrics
/// behaviors, CQRS handler, domain-event dispatcher, outbox publisher handler,
/// Mapperly-less mapper) — and asserts the Avro event reaches the transactional
/// outbox with the exact topic, key, and payload shape specified in
/// <c>events-catalog.md § 5.2.1</c>.
/// </summary>
/// <remarks>
/// The transactional outbox is stubbed with NSubstitute rather than a real
/// PostgreSQL / EF Core context: the <c>BasketDbContext</c> concrete class is a
/// deliverable of milestone M6, and a DB-backed version of this test belongs
/// there. The present test is scoped to "the Application pipeline wires end-to-end
/// and passes the right arguments to the outbox contract" — everything M4 owns.
/// </remarks>
public sealed class BasketCheckoutOutboxIntegrationTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 04, 23, 12, 00, 00, TimeSpan.Zero);

    private readonly ServiceProvider _provider;
    private readonly IBasketRepository _repo;
    private readonly IProductCatalogQueryPort _catalog;
    private readonly ITransactionalOutbox<IBasketDbContext> _outbox;
    private readonly DbContext _fakeDbContext;

    public BasketCheckoutOutboxIntegrationTests()
    {
        _repo = Substitute.For<IBasketRepository>();
        _catalog = Substitute.For<IProductCatalogQueryPort>();
        _outbox = Substitute.For<ITransactionalOutbox<IBasketDbContext>>();
        _outbox.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        // CheckoutBasketCommandHandler wraps the dispatch + outbox commit in
        // _outbox.Database.EnsureTransactionAsync(...). Provide a real (in-memory)
        // DbContext's Database facade so the wrap can no-op cleanly. SQL-level
        // transactional semantics are exercised by BasketCheckoutOutboxDbIntegrationTests
        // against a Postgres Testcontainer.
        _fakeDbContext = new InMemoryDbContextStub();
        _outbox.Database.Returns(_fakeDbContext.Database);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{TopicsOptions.Section}:{nameof(TopicsOptions.BasketSessions)}"] = "basket.sessions",
                [$"{TopicsOptions.Section}:{nameof(TopicsOptions.DltTopicSuffix)}"] = ".DLT",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));

        // Real Application DI — behaviors, handlers, dispatcher, validators.
        services.AddApplication();

        // Test seams for the two ports and the outbox.
        services.AddSingleton(_repo);
        services.AddSingleton(_catalog);
        services.AddSingleton(_outbox);

        _provider = services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public async Task CheckoutCommand_FullPipeline_WritesAvroEventToOutboxWithCorrectTopicAndKey()
    {
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();

        var basket = BasketAggregate.Create(userId, Now);
        basket.AddItem(productId, BasketTestData.Snapshot(amount: 19.9900m), 3, Now);
        _ = basket.PopDomainEvents();
        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(basket));
        _repo.SaveAsync(Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());
        _repo.DeleteAsync(userId, Arg.Any<CancellationToken>()).Returns(Result.Ok());

        using var scope = _provider.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CheckoutBasketCommand, Guid>>();

        var result = await handler.HandleAsync(
            new CheckoutBasketCommand(
                userId,
                correlationId,
                new CheckoutAddressDto
                {
                    Street1 = "1 Main St",
                    Street2 = "Apt 2",
                    City = "Springfield",
                    State = "IL",
                    PostalCode = "62704",
                    CountryCode = "US",
                },
                new CheckoutAddressDto
                {
                    Street1 = "Hlavní 10",
                    City = "Praha",
                    PostalCode = "11000",
                    CountryCode = "CZ",
                },
                paymentMethodId),
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Should().Be(correlationId);

            _outbox.Received(1).AddOutboxMessage(
                Arg.Is<string>(t => t == "basket.sessions"),
                Arg.Is<string>(k => k == userId.ToString()),
                Arg.Is<Basket.Sessions.BasketCheckoutInitiatedEvent>(e =>
                    e.BasketCorrelationId == correlationId
                    && e.UserId == userId
                    && e.PaymentMethodId == paymentMethodId
                    && e.Items.Count == 1
                    && e.Items[0].ProductId == productId
                    && e.Items[0].Quantity == 3
                    && e.Items[0].UnitPriceCurrency == "USD"
                    && e.Currency == "USD"
                    && e.ShippingAddress.CountryCode == "US"
                    && e.BillingAddress.CountryCode == "CZ"));

            await _outbox.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
            await _repo.Received(1).DeleteAsync(userId, Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task WhenTwoConcurrentCheckoutsForSameUser_ExactlyOneOutboxRowWritten()
    {
        // C-1 regression guard. Two parallel POSTs to /checkout for the same user with
        // different Idempotency-Keys both load the same basket. Without the CAS-save
        // wrap in CheckoutBasketCommandHandler, each handler invocation would dispatch
        // its domain event AND write an outbox row — producing two
        // BasketCheckoutInitiatedEvent records on basket.sessions for one user
        // (potential double charge in the Checkout saga). With the fix, the loser of
        // the CAS race gets BasketConcurrencyError (after one retry) and never reaches
        // the outbox.
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var basket = BasketAggregate.Create(userId, Now);
        basket.AddItem(productId, BasketTestData.Snapshot(amount: 19.9900m), 3, Now);
        _ = basket.PopDomainEvents();

        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(basket));
        _repo.DeleteAsync(userId, Arg.Any<CancellationToken>()).Returns(Result.Ok());

        // Simulate Redis CAS: exactly one SaveAsync wins; every subsequent attempt
        // sees the bumped version and fails BasketConcurrencyError. Thread-safe
        // because Interlocked.Increment serialises across the two parallel handlers.
        var saveCount = 0;
        _repo.SaveAsync(Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => System.Threading.Interlocked.Increment(ref saveCount) == 1
                ? Result.Ok()
                : Result.Fail(new BasketConcurrencyError(userId, Expected: 1, Actual: 2)));

        using var scope1 = _provider.CreateScope();
        using var scope2 = _provider.CreateScope();
        var handler1 = scope1.ServiceProvider
            .GetRequiredService<ICommandHandler<CheckoutBasketCommand, Guid>>();
        var handler2 = scope2.ServiceProvider
            .GetRequiredService<ICommandHandler<CheckoutBasketCommand, Guid>>();

        var cmd1 = MakeCommand(userId);
        var cmd2 = MakeCommand(userId);

        var task1 = handler1.HandleAsync(cmd1, TestContext.Current.CancellationToken);
        var task2 = handler2.HandleAsync(cmd2, TestContext.Current.CancellationToken);
        var results = await Task.WhenAll(task1, task2);

        using (new AssertionScope())
        {
            var successes = results.Count(r => r.IsSuccess);
            var failures = results.Count(r => r.IsFailed);
            successes.Should().Be(1, "exactly one checkout wins the CAS race");
            failures.Should().Be(1, "the loser surfaces BasketConcurrencyError after one retry");

            _outbox.Received(1).AddOutboxMessage(
                Arg.Is<string>(t => t == "basket.sessions"),
                Arg.Is<string>(k => k == userId.ToString()),
                Arg.Any<Basket.Sessions.BasketCheckoutInitiatedEvent>());
            await _outbox.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    private static CheckoutBasketCommand MakeCommand(Guid userId) => new(
        userId,
        Guid.CreateVersion7(),
        new CheckoutAddressDto
        {
            Street1 = "1 Main St",
            City = "Springfield",
            PostalCode = "62704",
            CountryCode = "US",
        },
        new CheckoutAddressDto
        {
            Street1 = "Hlavní 10",
            City = "Praha",
            PostalCode = "11000",
            CountryCode = "CZ",
        },
        Guid.CreateVersion7());

    [Fact]
    public async Task CheckoutCommand_WhenValidatorFails_ShortCircuitsBeforeOutbox()
    {
        using var scope = _provider.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CheckoutBasketCommand, Guid>>();

        var v4 = Guid.NewGuid(); // Version 4 — validator must reject

        var result = await handler.HandleAsync(
            new CheckoutBasketCommand(
                Guid.CreateVersion7(),
                v4,
                ApplicationTestDataForIntegration.ValidAddress(),
                ApplicationTestDataForIntegration.ValidAddress(),
                Guid.CreateVersion7()),
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            _outbox.DidNotReceiveWithAnyArgs().AddOutboxMessage(default!, default, default!);
            await _repo.DidNotReceive().GetByUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        }
    }

    public void Dispose()
    {
        _provider.Dispose();
        _fakeDbContext.Dispose();
    }

    private sealed class InMemoryDbContextStub : DbContext
    {
        public InMemoryDbContextStub()
            : base(new DbContextOptionsBuilder()
                .UseInMemoryDatabase(Guid.CreateVersion7().ToString())
                .ConfigureWarnings(b => b.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options)
        {
        }
    }
}

/// <summary>Minimal DTO builder for integration tests (mirrors the unit-test helper).</summary>
internal static class ApplicationTestDataForIntegration
{
    public static CheckoutAddressDto ValidAddress() => new()
    {
        Street1 = "1 Main St",
        City = "Springfield",
        PostalCode = "62704",
        CountryCode = "US",
    };
}

/// <summary>
/// Domain snapshot builder — duplicated from <c>Basket.UnitTests.Baskets.BasketTestData</c>
/// because that type lives in a different assembly and is <c>internal</c>. The duplication
/// is localised and small enough to not warrant a shared test-framework project yet.
/// </summary>
internal static class BasketTestData
{
    private static readonly DateTimeOffset DefaultCapturedAt =
        new(2026, 01, 15, 09, 30, 00, TimeSpan.Zero);

    public static ProductSnapshot Snapshot(decimal amount = 10m)
        => ProductSnapshot.Create(
            "SKU-1",
            "Product 1",
            new Platform.SharedKernel.ValueObjects.Money(
                amount, Platform.SharedKernel.ValueObjects.CurrencyCode.Usd),
            DefaultCapturedAt);
}
