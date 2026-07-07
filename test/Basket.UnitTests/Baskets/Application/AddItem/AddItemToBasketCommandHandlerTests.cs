using Basket.Application.Abstractions;
using Basket.Application.Baskets.AddItem;
using Basket.Application.Baskets.Common.Errors;
using Basket.Domain.Baskets.Errors;
using Basket.Domain.Baskets.ValueObjects;
using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Platform.SharedKernel.Base.DomainEvents;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.UnitTests.Baskets.Application.AddItem;

public class AddItemToBasketCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 04, 23, 12, 00, 00, TimeSpan.Zero);

    private readonly IBasketRepository _repo = Substitute.For<IBasketRepository>();
    private readonly IProductCatalogQueryPort _catalog = Substitute.For<IProductCatalogQueryPort>();
    private readonly IDomainEventDispatcher _dispatcher = Substitute.For<IDomainEventDispatcher>();
    private readonly FakeTimeProvider _time = new(Now);

    private AddItemToBasketCommandHandler CreateSut()
        => new(_repo, _catalog, _dispatcher, _time);

    [Fact]
    public async Task Handle_WhenNoBasket_CreatesNewBasketSavesAndDispatchesEvents()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var snapshot = BasketTestData.Snapshot();
        _catalog.GetProductSnapshotAsync(productId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(snapshot));
        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(null));
        _repo.SaveAsync(Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        // Act
        var result = await CreateSut().HandleAsync(
            new AddItemToBasketCommand(userId, productId, 2),
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            await _repo.Received(1).SaveAsync(
                Arg.Is<BasketAggregate>(b => b.UserId == userId && b.Items.Count == 1),
                0, // expectedVersion from a freshly Created basket
                Arg.Any<CancellationToken>());
            await _dispatcher.Received().DispatchAsync(
                Arg.Any<DomainEvent>(),
                Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_WhenCatalogReturnsNotFound_PropagatesError()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        _catalog.GetProductSnapshotAsync(productId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail<ProductSnapshot>(BasketAclErrors.ProductNotFound(productId)));

        // Act
        var result = await CreateSut().HandleAsync(
            new AddItemToBasketCommand(userId, productId, 1),
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            await _repo.DidNotReceive().GetByUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task Handle_WhenFirstSaveConflicts_ReloadsAndSucceedsOnSecondAttempt()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var snapshot = BasketTestData.Snapshot();
        _catalog.GetProductSnapshotAsync(productId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(snapshot));

        var existing = BasketAggregate.Create(userId, Now);
        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(existing));

        var saveCalls = 0;
        _repo.SaveAsync(Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                saveCalls++;
                return saveCalls == 1
                    ? Result.Fail(new BasketConcurrencyError(userId, expected: 0, actual: 1))
                    : Result.Ok();
            });

        // Act
        var result = await CreateSut().HandleAsync(
            new AddItemToBasketCommand(userId, productId, 1),
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
    public async Task Handle_WhenBothSavesConflict_PropagatesConcurrencyError()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        _catalog.GetProductSnapshotAsync(productId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(BasketTestData.Snapshot()));
        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(null));
        _repo.SaveAsync(Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail(new BasketConcurrencyError(userId, 0, 5)));

        // Act
        var result = await CreateSut().HandleAsync(
            new AddItemToBasketCommand(userId, productId, 1),
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.HasError<BasketConcurrencyError>().Should().BeTrue();
            await _repo.Received(2).SaveAsync(
                Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        }
    }
}
