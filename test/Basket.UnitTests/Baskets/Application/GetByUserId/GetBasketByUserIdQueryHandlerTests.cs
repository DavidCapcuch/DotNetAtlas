using Basket.Application.Abstractions;
using Basket.Application.Baskets.GetByUserId;
using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using NSubstitute;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.UnitTests.Baskets.Application.GetByUserId;

public class GetBasketByUserIdQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 04, 23, 12, 00, 00, TimeSpan.Zero);

    private readonly IBasketRepository _repo = Substitute.For<IBasketRepository>();

    private GetBasketByUserIdQueryHandler CreateSut() => new(_repo);

    [Fact]
    public async Task Handle_WhenAbsent_ReturnsEmptyResponseNotFailure()
    {
        var userId = Guid.CreateVersion7();
        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(null));

        var result = await CreateSut().HandleAsync(
            new GetBasketByUserIdQuery(userId),
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.UserId.Should().Be(userId);
            result.Value.Version.Should().Be(0);
            result.Value.Items.Should().BeEmpty();
            result.Value.Total.Should().BeNull();
        }
    }

    [Fact]
    public async Task Handle_WhenPresent_MapsEveryField()
    {
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var basket = BasketAggregate.Create(userId, Now);
        basket.AddItem(productId, BasketTestData.Snapshot(amount: 5m), 3, Now);
        _ = basket.PopDomainEvents();

        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(basket));

        var result = await CreateSut().HandleAsync(
            new GetBasketByUserIdQuery(userId),
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            var r = result.Value;
            r.UserId.Should().Be(userId);
            r.Version.Should().Be(basket.Version);
            r.Items.Should().ContainSingle();
            r.Items[0].ProductId.Should().Be(productId);
            r.Items[0].Quantity.Should().Be(3);
            r.Items[0].SnapshotPrice.Amount.Should().Be(5m);
            r.Items[0].SnapshotPrice.Currency.Should().Be("USD");
            r.Items[0].LineTotal.Amount.Should().Be(15m);
            r.Total.Should().NotBeNull();
            r.Total!.Amount.Should().Be(15m);
            r.Total.Currency.Should().Be("USD");
            r.CreatedAtUtc.Should().Be(Now);
            r.LastModifiedAtUtc.Should().Be(Now);
        }
    }
}
