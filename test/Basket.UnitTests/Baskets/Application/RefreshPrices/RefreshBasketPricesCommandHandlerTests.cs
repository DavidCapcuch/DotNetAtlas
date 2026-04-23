using Basket.Application.Abstractions;
using Basket.Application.Baskets.RefreshPrices;
using Basket.Domain.Baskets.Errors;
using Basket.Domain.Baskets.ValueObjects;
using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.ValueObjects;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.UnitTests.Baskets.Application.RefreshPrices;

public class RefreshBasketPricesCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 04, 23, 12, 00, 00, TimeSpan.Zero);

    private readonly IBasketRepository _repo = Substitute.For<IBasketRepository>();
    private readonly IProductCatalogQueryPort _catalog = Substitute.For<IProductCatalogQueryPort>();
    private readonly IDomainEventDispatcher _dispatcher = Substitute.For<IDomainEventDispatcher>();
    private readonly FakeTimeProvider _time = new(Now);

    private RefreshBasketPricesCommandHandler CreateSut() => new(_repo, _catalog, _dispatcher, _time);

    [Fact]
    public async Task Handle_WhenNoBasket_ReturnsOkWithoutCatalogCall()
    {
        var userId = Guid.CreateVersion7();
        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(null));

        var result = await CreateSut().HandleAsync(
            new RefreshBasketPricesCommand(userId),
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            await _catalog.DidNotReceive().GetManyAsync(
                Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_WhenPriceChanges_RefreshesSavesAndDispatchesEvent()
    {
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var basket = BasketAggregate.Create(userId, Now);
        basket.AddItem(productId, BasketTestData.Snapshot(amount: 10m), 1, Now);
        _ = basket.PopDomainEvents();
        var versionBefore = basket.Version;

        var updatedSnapshot = BasketTestData.Snapshot(amount: 15m);
        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(basket));
        _catalog.GetManyAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok<IReadOnlyList<(Guid, ProductSnapshot)>>(
                [(productId, updatedSnapshot)]));
        _repo.SaveAsync(Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        var result = await CreateSut().HandleAsync(
            new RefreshBasketPricesCommand(userId),
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            basket.Items.Single().Snapshot.Price.Amount.Should().Be(15m);
            await _repo.Received(1).SaveAsync(basket, versionBefore, Arg.Any<CancellationToken>());
            await _dispatcher.Received(1).DispatchAsync(
                Arg.Any<DomainEvent>(), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_WhenCatalogUnavailable_PropagatesError()
    {
        var userId = Guid.CreateVersion7();
        var basket = BasketAggregate.Create(userId, Now);
        basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(), 1, Now);
        _ = basket.PopDomainEvents();

        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(basket));
        _catalog.GetManyAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<IReadOnlyList<(Guid, ProductSnapshot)>>(
                BasketErrors.CatalogUnavailable()));

        var result = await CreateSut().HandleAsync(
            new RefreshBasketPricesCommand(userId),
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors[0].Should().BeOfType<ValidationError>()
                .Which.ErrorCode.Should().Be("Basket.CatalogUnavailable");
            await _repo.DidNotReceive().SaveAsync(
                Arg.Any<BasketAggregate>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        }
    }
}
