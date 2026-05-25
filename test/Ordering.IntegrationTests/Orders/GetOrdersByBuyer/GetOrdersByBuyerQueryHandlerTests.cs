using Microsoft.Extensions.DependencyInjection;
using Ordering.Application.Common.Data;
using Ordering.Application.Orders.GetOrdersByBuyer;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Persistence.Database;
using Ordering.IntegrationTests.Common;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.IntegrationTests.Orders.GetOrdersByBuyer;

/// <summary>
/// Integration tests for <see cref="GetOrdersByBuyerQueryHandler"/>'s
/// SQL-side projection to <see cref="OrderSummaryDto"/>
/// (<c>use-cases.md § 3.4.2</c>). EF Core's InMemory provider cannot
/// translate the conditional projection on the owned <c>Shipment</c> VO
/// nor the <c>?? → COALESCE</c> chain for
/// <c>LastStatusChangeAtUtc</c> — so these run against the real Postgres
/// container (ADR-0021).
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
    public async Task Handle_projects_summary_shape_for_a_Created_order()
    {
        var buyerId = Guid.CreateVersion7();
        var ct = TestContext.Current.CancellationToken;

        Order seeded;
        using (var seedScope = _fixture.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(dbContext, _fixture.FakeTime);
            seeded = await seed.CreateOrderAsync(buyerId: buyerId, cancellationToken: ct);
        }

        var result = await ExecuteHandlerAsync(buyerId, ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle();
        var dto = result.Value.Items[0];
        dto.OrderId.Should().Be(seeded.Id);
        dto.Status.Should().Be(OrderStatus.Created.Name);
        dto.TotalAmount.Should().Be(19.98m);
        dto.Currency.Should().Be(CurrencyCode.Eur.Name);
        dto.ItemCount.Should().Be(1);
        dto.CreatedAtUtc.Should().Be(seeded.CreatedAtUtc);
        // LastStatusChangeAtUtc falls back to CreatedAtUtc — no later
        // transition timestamps populated.
        dto.LastStatusChangeAtUtc.Should().Be(seeded.CreatedAtUtc);
    }

    [Fact]
    public async Task Handle_returns_only_callers_orders()
    {
        var buyerId = Guid.CreateVersion7();
        var otherBuyerId = Guid.CreateVersion7();
        var ct = TestContext.Current.CancellationToken;

        Order ownA;
        Order ownB;
        Order someoneElses;
        using (var seedScope = _fixture.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(dbContext, _fixture.FakeTime);
            ownA = await seed.CreateOrderAsync(buyerId: buyerId, cancellationToken: ct);
            ownB = await seed.CreateOrderAsync(buyerId: buyerId, cancellationToken: ct);
            someoneElses = await seed.CreateOrderAsync(buyerId: otherBuyerId, cancellationToken: ct);
        }

        var result = await ExecuteHandlerAsync(buyerId, ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Total.Should().Be(2);
        result.Value.Items.Select(i => i.OrderId).Should()
            .BeEquivalentTo(new[] { ownA.Id, ownB.Id });
        result.Value.Items.Should().NotContain(i => i.OrderId == someoneElses.Id);
    }

    [Fact]
    public async Task Handle_pages_through_results_and_reports_unbounded_Total()
    {
        var buyerId = Guid.CreateVersion7();
        var ct = TestContext.Current.CancellationToken;

        using (var seedScope = _fixture.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(dbContext, _fixture.FakeTime);
            for (var i = 0; i < 5; i++)
            {
                await seed.CreateOrderAsync(buyerId: buyerId, cancellationToken: ct);
            }
        }

        var page1 = await ExecuteHandlerAsync(buyerId, ct, pageNumber: 1, pageSize: 2);
        var page3 = await ExecuteHandlerAsync(buyerId, ct, pageNumber: 3, pageSize: 2);

        page1.IsSuccess.Should().BeTrue();
        page1.Value.Total.Should().Be(5);
        page1.Value.PageNumber.Should().Be(1);
        page1.Value.PageSize.Should().Be(2);
        page1.Value.Items.Should().HaveCount(2);

        page3.IsSuccess.Should().BeTrue();
        page3.Value.Total.Should().Be(5);
        page3.Value.PageNumber.Should().Be(3);
        page3.Value.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_filters_by_Status()
    {
        var buyerId = Guid.CreateVersion7();
        var ct = TestContext.Current.CancellationToken;
        const string reason = "Buyer requested cancellation";

        Order cancelledOrder;
        using (var seedScope = _fixture.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(dbContext, _fixture.FakeTime);
            await seed.CreateOrderAsync(buyerId: buyerId, cancellationToken: ct); // stays Created
            cancelledOrder = await seed.CreateOrderAsync(buyerId: buyerId, cancellationToken: ct);
            cancelledOrder.Cancel(reason, _fixture.FakeTime.GetUtcNow()).IsSuccess.Should().BeTrue();
            await dbContext.SaveChangesAsync(ct);
        }

        var result = await ExecuteHandlerAsync(buyerId, ct, status: OrderStatus.Cancelled.Name);

        result.IsSuccess.Should().BeTrue();
        result.Value.Total.Should().Be(1);
        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].OrderId.Should().Be(cancelledOrder.Id);
        result.Value.Items[0].Status.Should().Be(OrderStatus.Cancelled.Name);
    }

    [Theory]
    [InlineData(LifecycleState.Created)]
    [InlineData(LifecycleState.StockReserved)]
    [InlineData(LifecycleState.PaymentCompleted)]
    [InlineData(LifecycleState.Confirmed)]
    [InlineData(LifecycleState.Shipped)]
    [InlineData(LifecycleState.Delivered)]
    public async Task Handle_LastStatusChangeAtUtc_picks_most_recent_lifecycle_timestamp(LifecycleState target)
    {
        var buyerId = Guid.CreateVersion7();
        var ct = TestContext.Current.CancellationToken;

        var seeded = await SeedOrderAdvancingTimeAsync(buyerId, target, ct);

        var expected = ExpectedLastStatusChangeAtUtc(seeded, target);

        var result = await ExecuteHandlerAsync(buyerId, ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].LastStatusChangeAtUtc.Should().Be(expected);
        result.Value.Items[0].Status.Should().Be(ExpectedStatus(target).Name);
    }

    private async Task<FluentResults.Result<GetOrdersByBuyerResponse>> ExecuteHandlerAsync(
        Guid buyerId,
        CancellationToken ct,
        int pageNumber = 1,
        int pageSize = 10,
        string? status = null)
    {
        using var queryScope = _fixture.CreateScope();
        var queryContext = queryScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var handler = new GetOrdersByBuyerQueryHandler((IOrderingDbContext)queryContext);
        return await handler.HandleAsync(
            new GetOrdersByBuyerQuery
            {
                BuyerId = buyerId,
                PageNumber = pageNumber,
                PageSize = pageSize,
                Status = status,
            },
            ct);
    }

    /// <summary>
    /// Walks the order through the FSM up to <paramref name="target"/>,
    /// advancing the fake clock by one minute between transitions so every
    /// lifecycle timestamp is distinct. This is what lets the COALESCE
    /// chain test assert the projection picked the <em>right</em> field,
    /// not just any non-null one.
    /// </summary>
    private async Task<Order> SeedOrderAdvancingTimeAsync(Guid buyerId, LifecycleState target, CancellationToken ct)
    {
        using var seedScope = _fixture.CreateScope();
        var dbContext = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var seed = new OrderSeed(dbContext, _fixture.FakeTime);

        var order = await seed.CreateOrderAsync(buyerId: buyerId, cancellationToken: ct);
        if (target == LifecycleState.Created)
        {
            return order;
        }

        _fixture.FakeTime.Advance(TimeSpan.FromMinutes(1));
        order.MarkStockReserved(Guid.CreateVersion7(), _fixture.FakeTime.GetUtcNow()).IsSuccess.Should().BeTrue();
        if (target == LifecycleState.StockReserved)
        {
            await dbContext.SaveChangesAsync(ct);
            return order;
        }

        _fixture.FakeTime.Advance(TimeSpan.FromMinutes(1));
        order.MarkPaymentCompleted(Guid.CreateVersion7(), _fixture.FakeTime.GetUtcNow()).IsSuccess.Should().BeTrue();
        if (target == LifecycleState.PaymentCompleted)
        {
            await dbContext.SaveChangesAsync(ct);
            return order;
        }

        _fixture.FakeTime.Advance(TimeSpan.FromMinutes(1));
        order.Confirm(_fixture.FakeTime.GetUtcNow()).IsSuccess.Should().BeTrue();
        if (target == LifecycleState.Confirmed)
        {
            await dbContext.SaveChangesAsync(ct);
            return order;
        }

        _fixture.FakeTime.Advance(TimeSpan.FromMinutes(1));
        order.MarkShipped("DHL", "1Z999AA10123456784", _fixture.FakeTime.GetUtcNow()).IsSuccess.Should().BeTrue();
        if (target == LifecycleState.Shipped)
        {
            await dbContext.SaveChangesAsync(ct);
            return order;
        }

        _fixture.FakeTime.Advance(TimeSpan.FromMinutes(1));
        order.MarkDelivered(_fixture.FakeTime.GetUtcNow()).IsSuccess.Should().BeTrue();
        await dbContext.SaveChangesAsync(ct);
        return order;
    }

    private static DateTimeOffset ExpectedLastStatusChangeAtUtc(Order order, LifecycleState target) =>
        target switch
        {
            LifecycleState.Created => order.CreatedAtUtc,
            LifecycleState.StockReserved => order.StockReservedAtUtc!.Value,
            LifecycleState.PaymentCompleted => order.PaymentCompletedAtUtc!.Value,
            LifecycleState.Confirmed => order.ConfirmedAtUtc!.Value,
            LifecycleState.Shipped => order.Shipment!.ShippedAtUtc,
            LifecycleState.Delivered => order.DeliveredAtUtc!.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

    private static OrderStatus ExpectedStatus(LifecycleState target) =>
        target switch
        {
            LifecycleState.Created => OrderStatus.Created,
            LifecycleState.StockReserved => OrderStatus.StockReserved,
            LifecycleState.PaymentCompleted => OrderStatus.PaymentCompleted,
            LifecycleState.Confirmed => OrderStatus.Confirmed,
            LifecycleState.Shipped => OrderStatus.Shipped,
            LifecycleState.Delivered => OrderStatus.Delivered,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

    public enum LifecycleState
    {
        Created,
        StockReserved,
        PaymentCompleted,
        Confirmed,
        Shipped,
        Delivered,
    }
}
