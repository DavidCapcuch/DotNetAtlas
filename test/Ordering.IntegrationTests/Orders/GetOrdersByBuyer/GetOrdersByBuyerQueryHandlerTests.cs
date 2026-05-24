using Microsoft.Extensions.DependencyInjection;
using Ordering.Application.Common.Data;
using Ordering.Application.Orders.GetOrdersByBuyer;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Persistence.Database;
using Ordering.IntegrationTests.Common;

namespace Ordering.IntegrationTests.Orders.GetOrdersByBuyer;

/// <summary>
/// Characterisation tests for <see cref="GetOrdersByBuyerQueryHandler"/>'s
/// projection of the optional <c>CancellationInfo</c>/<c>FailureInfo</c>/
/// <c>ShipmentInfo</c> VOs (issue #238). One test per terminal optional-VO
/// shape: none populated (Created), only Cancellation, only Failure, only
/// Shipment. The same four assertions must pass against both the legacy
/// materialise-then-project handler AND the SQL-side-projection rewrite.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class GetOrdersByBuyerQueryHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public GetOrdersByBuyerQueryHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Handle_returns_order_with_no_optional_VOs_when_order_is_active()
    {
        var buyerId = Guid.CreateVersion7();
        var ct = TestContext.Current.CancellationToken;

        using (var seedScope = _fixture.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(dbContext, _fixture.FakeTime);
            await seed.CreateOrderAsync(buyerId: buyerId, cancellationToken: ct);
        }

        using var queryScope = _fixture.CreateScope();
        var queryContext = queryScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var handler = new GetOrdersByBuyerQueryHandler((IOrderingDbContext)queryContext);

        var result = await handler.HandleAsync(
            new GetOrdersByBuyerQuery { BuyerId = buyerId, Take = 10 },
            ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Orders.Should().ContainSingle();
        var dto = result.Value.Orders[0];
        dto.BuyerId.Should().Be(buyerId);
        dto.Status.Should().Be(OrderStatus.Created.Name);
        dto.Cancellation.Should().BeNull();
        dto.Failure.Should().BeNull();
        dto.Shipment.Should().BeNull();
        dto.Items.Should().ContainSingle();
        dto.ShippingAddress.City.Should().Be("Prague");
        dto.BillingAddress.PostalCode.Should().Be("11000");
        dto.TotalAmount.Should().Be(19.98m);
        dto.Currency.Should().Be(Platform.SharedKernel.ValueObjects.CurrencyCode.Eur.Name);
    }

    [Fact]
    public async Task Handle_returns_order_with_only_Cancellation_populated_when_order_is_cancelled()
    {
        var buyerId = Guid.CreateVersion7();
        var ct = TestContext.Current.CancellationToken;
        const string reason = "Buyer requested cancellation";
        var cancelledAtUtc = _fixture.FakeTime.GetUtcNow();

        using (var seedScope = _fixture.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(dbContext, _fixture.FakeTime);
            var order = await seed.CreateOrderAsync(buyerId: buyerId, cancellationToken: ct);
            order.Cancel(reason, cancelledAtUtc).IsSuccess.Should().BeTrue();
            await dbContext.SaveChangesAsync(ct);
        }

        using var queryScope = _fixture.CreateScope();
        var queryContext = queryScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var handler = new GetOrdersByBuyerQueryHandler((IOrderingDbContext)queryContext);

        var result = await handler.HandleAsync(
            new GetOrdersByBuyerQuery { BuyerId = buyerId, Take = 10 },
            ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Orders.Should().ContainSingle();
        var dto = result.Value.Orders[0];
        dto.Status.Should().Be(OrderStatus.Cancelled.Name);
        dto.Cancellation.Should().NotBeNull();
        dto.Cancellation!.Reason.Should().Be(reason);
        dto.Cancellation.AtStatus.Should().Be(OrderStatus.Created.Name);
        dto.Cancellation.CancelledAtUtc.Should().Be(cancelledAtUtc);
        dto.Failure.Should().BeNull();
        dto.Shipment.Should().BeNull();
    }

    [Fact]
    public async Task Handle_returns_order_with_only_Failure_populated_when_order_is_failed()
    {
        var buyerId = Guid.CreateVersion7();
        var ct = TestContext.Current.CancellationToken;
        const string errorCode = "STOCK_UNAVAILABLE";
        const string errorMessage = "Insufficient stock for requested items";
        var failedAtUtc = _fixture.FakeTime.GetUtcNow();

        using (var seedScope = _fixture.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(dbContext, _fixture.FakeTime);
            var order = await seed.CreateOrderAsync(buyerId: buyerId, cancellationToken: ct);
            order.Fail(errorCode, errorMessage, failedAtUtc).IsSuccess.Should().BeTrue();
            await dbContext.SaveChangesAsync(ct);
        }

        using var queryScope = _fixture.CreateScope();
        var queryContext = queryScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var handler = new GetOrdersByBuyerQueryHandler((IOrderingDbContext)queryContext);

        var result = await handler.HandleAsync(
            new GetOrdersByBuyerQuery { BuyerId = buyerId, Take = 10 },
            ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Orders.Should().ContainSingle();
        var dto = result.Value.Orders[0];
        dto.Status.Should().Be(OrderStatus.Failed.Name);
        dto.Failure.Should().NotBeNull();
        dto.Failure!.ErrorCode.Should().Be(errorCode);
        dto.Failure.ErrorMessage.Should().Be(errorMessage);
        dto.Failure.AtStatus.Should().Be(OrderStatus.Created.Name);
        dto.Failure.FailedAtUtc.Should().Be(failedAtUtc);
        dto.Cancellation.Should().BeNull();
        dto.Shipment.Should().BeNull();
    }

    [Fact]
    public async Task Handle_returns_order_with_only_Shipment_populated_when_order_is_shipped()
    {
        var buyerId = Guid.CreateVersion7();
        var ct = TestContext.Current.CancellationToken;
        var shippedAtUtc = _fixture.FakeTime.GetUtcNow();

        using (var seedScope = _fixture.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(dbContext, _fixture.FakeTime);
            await seed.CreateShippedOrderAsync(buyerId: buyerId, cancellationToken: ct);
        }

        using var queryScope = _fixture.CreateScope();
        var queryContext = queryScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var handler = new GetOrdersByBuyerQueryHandler((IOrderingDbContext)queryContext);

        var result = await handler.HandleAsync(
            new GetOrdersByBuyerQuery { BuyerId = buyerId, Take = 10 },
            ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Orders.Should().ContainSingle();
        var dto = result.Value.Orders[0];
        dto.Status.Should().Be(OrderStatus.Shipped.Name);
        dto.Shipment.Should().NotBeNull();
        dto.Shipment!.Carrier.Should().Be("DHL");
        dto.Shipment.TrackingNumber.Should().Be("1Z999AA10123456784");
        dto.Shipment.ShippedAtUtc.Should().Be(shippedAtUtc);
        dto.Cancellation.Should().BeNull();
        dto.Failure.Should().BeNull();
    }
}
