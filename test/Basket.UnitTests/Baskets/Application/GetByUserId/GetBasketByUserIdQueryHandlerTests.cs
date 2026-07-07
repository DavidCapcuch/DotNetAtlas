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
        // Arrange
        var userId = Guid.CreateVersion7();
        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(null));

        // Act
        var result = await CreateSut().HandleAsync(
            new GetBasketByUserIdQuery(userId),
            TestContext.Current.CancellationToken);

        // Assert
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
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var basket = BasketAggregate.Create(userId, Now);
        basket.AddItem(productId, BasketTestData.Snapshot(amount: 5m), 3, Now);
        _ = basket.PopDomainEvents();

        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(basket));

        // Act
        var result = await CreateSut().HandleAsync(
            new GetBasketByUserIdQuery(userId),
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            var response = result.Value;
            response.UserId.Should().Be(userId);
            response.Version.Should().Be(basket.Version);
            response.Items.Should().ContainSingle();
            response.Items[0].ProductId.Should().Be(productId);
            response.Items[0].Quantity.Should().Be(3);
            response.Items[0].SnapshotPrice.Amount.Should().Be(5m);
            response.Items[0].SnapshotPrice.Currency.Should().Be("USD");
            response.Items[0].LineTotal.Amount.Should().Be(15m);
            response.Total.Should().NotBeNull();
            response.Total!.Amount.Should().Be(15m);
            response.Total.Currency.Should().Be("USD");
            response.CreatedAtUtc.Should().Be(Now);
            response.LastModifiedAtUtc.Should().Be(Now);
        }
    }
}
