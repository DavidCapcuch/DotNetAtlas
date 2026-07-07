using Basket.Application.Abstractions;
using Basket.Application.Baskets.Checkout;
using Basket.Application.Common.Data;
using Basket.Domain.Baskets.Errors;
using Basket.Domain.Baskets.Events;
using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Errors;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.UnitTests.Baskets.Application.Checkout;

public class CheckoutBasketCommandHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 04, 23, 12, 00, 00, TimeSpan.Zero);

    private readonly IBasketRepository _repo = Substitute.For<IBasketRepository>();
    private readonly ITransactionalOutbox<IBasketDbContext> _outbox =
        Substitute.For<ITransactionalOutbox<IBasketDbContext>>();
    private readonly IDomainEventDispatcher _dispatcher = Substitute.For<IDomainEventDispatcher>();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly DbContext _fakeDbContext = new InMemoryDbContextStub();

    public CheckoutBasketCommandHandlerTests()
    {
        // CheckoutBasketCommandHandler wraps the dispatch + outbox commit in
        // _outbox.Database.EnsureTransactionAsync(...). Without a Database substitute
        // every test would NRE on the wrap. Wire an EF InMemory DbContext whose
        // BeginTransactionAsync is a configured no-op so the wrap simply invokes the
        // lambda. Real transactional semantics are exercised by
        // BasketCheckoutOutboxIntegrationTests against a Postgres Testcontainer.
        _outbox.Database.Returns(_fakeDbContext.Database);
    }

    public void Dispose() => _fakeDbContext.Dispose();

    private CheckoutBasketCommandHandler CreateSut() => new(
        _repo,
        _outbox,
        _dispatcher,
        _time,
        NullLogger<CheckoutBasketCommandHandler>.Instance);

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

    private static CheckoutBasketCommand ValidCommand(Guid userId) => new(
        userId,
        ApplicationTestData.AddressDto(),
        ApplicationTestData.AddressDto("CZ"),
        Guid.CreateVersion7());

    [Fact]
    public async Task Handle_WhenBasketExistsWithItems_PersistsViaCasSavesOutboxAndDeletesRedis()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var basket = BasketAggregate.Create(userId, Now);
        basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(), 2, Now);
        _ = basket.PopDomainEvents();
        var expectedVersion = basket.Version;

        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(basket));
        _repo.SaveAsync(Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());
        _outbox.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        _repo.DeleteAsync(userId, Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var command = ValidCommand(userId);

        // Act
        var result = await CreateSut().HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            // Handler allocates the pre-assigned OrderId (ADR-0029); it is returned to the caller.
            result.Value.Should().NotBe(Guid.Empty);

            // CAS guard: SaveAsync is called with the version captured BEFORE Checkout().
            // Without this, two parallel checkouts would each write an outbox row.
            await _repo.Received(1).SaveAsync(
                Arg.Is<BasketAggregate>(b => b.UserId == userId),
                expectedVersion,
                Arg.Any<CancellationToken>());
            await _dispatcher.Received(1).DispatchAsync(
                Arg.Any<DomainEvent>(), Arg.Any<CancellationToken>());
            await _outbox.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
            await _repo.Received(1).DeleteAsync(userId, Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_AllocatesVersion7OrderId_AndCarriesItOnTheDispatchedEvent()
    {
        // ADR-0029: the OrderId is pre-assigned by Basket's checkout handler (UUID v7),
        // not supplied by the caller. The same id flows onto BasketCheckedOutDomainEvent
        // so the outbox publisher stamps it onto the integration event.

        // Arrange
        var userId = Guid.CreateVersion7();
        var basket = BasketAggregate.Create(userId, Now);
        basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(), 1, Now);
        _ = basket.PopDomainEvents();

        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(basket));
        _repo.SaveAsync(Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());
        _outbox.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        _repo.DeleteAsync(userId, Arg.Any<CancellationToken>()).Returns(Result.Ok());

        // Act
        var result = await CreateSut().HandleAsync(
            ValidCommand(userId),
            TestContext.Current.CancellationToken);

        // Assert
        var dispatched = _dispatcher.ReceivedCalls()
            .Select(c => c.GetArguments()[0])
            .OfType<BasketCheckedOutDomainEvent>()
            .Single();

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Should().NotBe(Guid.Empty);
            result.Value.Version.Should().Be(7, "the pre-assigned OrderId is a UUID v7");
            dispatched.OrderId.Should().Be(result.Value);
        }
    }

    [Fact]
    public async Task Handle_WhenBasketMissing_FailsEmptyBasket()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(null));

        // Act
        var result = await CreateSut().HandleAsync(
            ValidCommand(userId),
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors[0].Should().BeOfType<ConflictError>()
                .Which.ErrorCode.Should().Be("Basket.Empty");
            await _outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
            await _repo.DidNotReceive().SaveAsync(
                Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_WhenBasketEmpty_FailsEmptyBasket()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var basket = BasketAggregate.Create(userId, Now);
        _ = basket.PopDomainEvents();
        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(basket));

        // Act
        var result = await CreateSut().HandleAsync(
            ValidCommand(userId),
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors[0].Should().BeOfType<ConflictError>()
                .Which.ErrorCode.Should().Be("Basket.Empty");
            await _repo.DidNotReceive().SaveAsync(
                Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task Handle_WhenRedisDeleteFails_StillReturnsSuccess()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var basket = BasketAggregate.Create(userId, Now);
        basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(), 1, Now);
        _ = basket.PopDomainEvents();

        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(basket));
        _repo.SaveAsync(Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());
        _outbox.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        _repo.DeleteAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail("redis transient"));

        // Act
        var result = await CreateSut().HandleAsync(
            ValidCommand(userId),
            TestContext.Current.CancellationToken);

        // Assert — outbox is source of truth; Redis delete failure does NOT fail the command.
        result.Should().BeSuccess();
    }

    [Fact]
    [Trait("Category", "resilience")]
    [Trait("Category", "regression")]
    public async Task Handle_WhenFirstSaveConflicts_RetriesOnceAndSucceeds()
    {
        // The C-1 fix relies on BasketConcurrencyRetry — exactly one retry on CAS loss.
        // This pins the policy at the handler level so a future refactor that drops the
        // retry wrap fails loudly here. Each attempt MUST reload the aggregate so the
        // second try operates on the winner's persisted state.

        // Arrange
        var userId = Guid.CreateVersion7();
        var basket = BasketAggregate.Create(userId, Now);
        basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(), 1, Now);
        _ = basket.PopDomainEvents();

        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(basket));

        var saveCalls = 0;
        _repo.SaveAsync(Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                saveCalls++;
                return saveCalls == 1
                    ? Result.Fail(new BasketConcurrencyError(userId, expected: 1, actual: 2))
                    : Result.Ok();
            });
        _outbox.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        _repo.DeleteAsync(userId, Arg.Any<CancellationToken>()).Returns(Result.Ok());

        // Act
        var result = await CreateSut().HandleAsync(
            ValidCommand(userId),
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            saveCalls.Should().Be(2);
            await _repo.Received(2).GetByUserIdAsync(userId, Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    [Trait("Category", "regression")]
    public async Task Handle_WhenBothSavesConflict_PropagatesConcurrencyError_AndNoOutboxRowWritten()
    {
        // C-1 fail-loud surface: when CAS loses twice, the loser MUST NOT emit an
        // integration event. Otherwise two parallel checkouts would still produce two
        // BasketCheckoutInitiatedEvent records on the basket.sessions topic.

        // Arrange
        var userId = Guid.CreateVersion7();
        var basket = BasketAggregate.Create(userId, Now);
        basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(), 1, Now);
        _ = basket.PopDomainEvents();

        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(basket));
        _repo.SaveAsync(Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail(new BasketConcurrencyError(userId, 1, 9)));

        // Act
        var result = await CreateSut().HandleAsync(
            ValidCommand(userId),
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.HasError<BasketConcurrencyError>().Should().BeTrue();
            await _repo.Received(2).SaveAsync(
                Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
            await _outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
            await _repo.DidNotReceive().DeleteAsync(userId, Arg.Any<CancellationToken>());
        }
    }
}
