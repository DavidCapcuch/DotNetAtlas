using Basket.Application.Abstractions;
using Basket.Application.Baskets.ChangeItemQuantity;
using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Errors;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.UnitTests.Baskets.Application.ChangeItemQuantity;

public class ChangeItemQuantityCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 04, 23, 12, 00, 00, TimeSpan.Zero);

    private readonly IBasketRepository _repo = Substitute.For<IBasketRepository>();
    private readonly IDomainEventDispatcher _dispatcher = Substitute.For<IDomainEventDispatcher>();
    private readonly FakeTimeProvider _time = new(Now);

    private ChangeItemQuantityCommandHandler CreateSut() => new(_repo, _dispatcher, _time);

    [Fact]
    public async Task Handle_WhenNoBasket_FailsItemNotFound()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(null));

        // Act
        var result = await CreateSut().HandleAsync(
            new ChangeItemQuantityCommand(userId, Guid.CreateVersion7(), 3),
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors[0].Should().BeOfType<NotFoundError>()
                .Which.ErrorCode.Should().Be("Basket.ItemNotFound");
        }
    }

    [Fact]
    public async Task Handle_WhenItemPresent_UpdatesQuantityAndSaves()
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
            new ChangeItemQuantityCommand(userId, productId, 5),
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            basket.Items.Should().ContainSingle(i => i.Quantity == 5);
            await _repo.Received(1).SaveAsync(basket, versionBefore, Arg.Any<CancellationToken>());
            await _dispatcher.Received(1).DispatchAsync(
                Arg.Any<DomainEvent>(), Arg.Any<CancellationToken>());
        }
    }
}
