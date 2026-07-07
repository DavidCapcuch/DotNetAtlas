using Basket.Application.Abstractions;
using Basket.Application.Baskets.RemoveItem;
using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Platform.SharedKernel.Base.DomainEvents;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.UnitTests.Baskets.Application.RemoveItem;

public class RemoveItemFromBasketCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 04, 23, 12, 00, 00, TimeSpan.Zero);

    private readonly IBasketRepository _repo = Substitute.For<IBasketRepository>();
    private readonly IDomainEventDispatcher _dispatcher = Substitute.For<IDomainEventDispatcher>();
    private readonly FakeTimeProvider _time = new(Now);

    private RemoveItemFromBasketCommandHandler CreateSut() => new(_repo, _dispatcher, _time);

    [Fact]
    public async Task Handle_WhenNoBasket_ReturnsOkIdempotent()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(null));

        // Act
        var result = await CreateSut().HandleAsync(
            new RemoveItemFromBasketCommand(userId, Guid.CreateVersion7()),
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            await _repo.DidNotReceive().SaveAsync(
                Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_WhenItemMissing_ReturnsOkWithoutSaving()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var basket = BasketAggregate.Create(userId, Now);
        basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(), 1, Now);
        _ = basket.PopDomainEvents();
        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(basket));

        // Act
        var result = await CreateSut().HandleAsync(
            new RemoveItemFromBasketCommand(userId, Guid.CreateVersion7()),
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            await _repo.DidNotReceive().SaveAsync(
                Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_WhenItemPresent_SavesAndDispatchesEvent()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var basket = BasketAggregate.Create(userId, Now);
        basket.AddItem(productId, BasketTestData.Snapshot(), 1, Now);
        _ = basket.PopDomainEvents();
        var versionBefore = basket.Version;

        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(basket));
        _repo.SaveAsync(Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        // Act
        var result = await CreateSut().HandleAsync(
            new RemoveItemFromBasketCommand(userId, productId),
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            await _repo.Received(1).SaveAsync(
                Arg.Is<BasketAggregate>(b => b.Items.Count == 0),
                versionBefore,
                Arg.Any<CancellationToken>());
            await _dispatcher.Received(1).DispatchAsync(
                Arg.Any<DomainEvent>(), Arg.Any<CancellationToken>());
        }
    }
}
