using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ordering.Application.Common.Data;
using Ordering.Application.Orders.GetOrderById;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Persistence.Database;
using Ordering.IntegrationTests.Common;

namespace Ordering.IntegrationTests.Orders.GetOrderById;

/// <summary>
/// Characterisation tests for <see cref="GetOrderByIdQueryHandler"/>'s projection of the
/// optional <c>CancellationInfo</c>/<c>FailureInfo</c>/<c>ShipmentInfo</c> VOs and the
/// authorization branches (issue #277). The same assertions must pass against both the
/// legacy <c>WithSpecification(OrderByIdSpec)</c> handler AND the SQL-side-projection
/// rewrite that drops Ardalis.Specification on the read side.
/// </summary>
/// <remarks>
/// Lives at the integration tier (not the InMemory unit tier) because the InMemory
/// provider ignores VOs / SmartEnums / owned-type properties — see the note on
/// <c>test/Ordering.UnitTests/Application/Orders/GetOrderById/GetOrderByIdQueryHandlerTests.cs</c>.
/// </remarks>
[Collection<IntegrationTestCollection>]
public sealed class GetOrderByIdQueryHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public GetOrderByIdQueryHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    [Trait("Category", "critical-path")]
    public async Task Handle_returns_order_with_no_optional_VOs_when_order_is_active()
    {
        var buyerId = Guid.CreateVersion7();
        var ct = TestContext.Current.CancellationToken;

        Order seeded;
        using (var seedScope = _fixture.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(dbContext, TimeProvider.System);
            seeded = await seed.CreateOrderAsync(buyerId: buyerId, cancellationToken: ct);
        }

        var result = await InvokeHandlerAsync(seeded.Id, buyerId, isAdmin: false, ct);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        using (new AssertionScope())
        {
            dto.OrderId.Should().Be(seeded.Id);
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
    }

    [Fact]
    public async Task Handle_returns_order_with_only_Cancellation_populated_when_order_is_cancelled()
    {
        var buyerId = Guid.CreateVersion7();
        var ct = TestContext.Current.CancellationToken;
        const string reason = "Buyer requested cancellation";
        var cancelledAtUtc = DateTimeOffset.UtcNow;

        Order seeded;
        using (var seedScope = _fixture.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(dbContext, TimeProvider.System);
            seeded = await seed.CreateOrderAsync(buyerId: buyerId, cancellationToken: ct);
            seeded.Cancel(reason, cancelledAtUtc).IsSuccess.Should().BeTrue();
            await dbContext.SaveChangesAsync(ct);
        }

        var result = await InvokeHandlerAsync(seeded.Id, buyerId, isAdmin: false, ct);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        using (new AssertionScope())
        {
            dto.Status.Should().Be(OrderStatus.Cancelled.Name);
            dto.Cancellation.Should().NotBeNull();
            dto.Cancellation!.Reason.Should().Be(reason);
            dto.Cancellation.AtStatus.Should().Be(OrderStatus.Created.Name);
            // Postgres timestamptz truncates 100-ns precision to microseconds.
            dto.Cancellation.CancelledAtUtc.Should().BeCloseTo(cancelledAtUtc, TimeSpan.FromSeconds(1));
            dto.Failure.Should().BeNull();
            dto.Shipment.Should().BeNull();
        }
    }

    [Fact]
    public async Task Handle_returns_order_with_only_Failure_populated_when_order_is_failed()
    {
        var buyerId = Guid.CreateVersion7();
        var ct = TestContext.Current.CancellationToken;
        const string errorCode = "STOCK_UNAVAILABLE";
        const string errorMessage = "Insufficient stock for requested items";
        var failedAtUtc = DateTimeOffset.UtcNow;

        Order seeded;
        using (var seedScope = _fixture.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(dbContext, TimeProvider.System);
            seeded = await seed.CreateOrderAsync(buyerId: buyerId, cancellationToken: ct);
            seeded.Fail(errorCode, errorMessage, failedAtUtc).IsSuccess.Should().BeTrue();
            await dbContext.SaveChangesAsync(ct);
        }

        var result = await InvokeHandlerAsync(seeded.Id, buyerId, isAdmin: false, ct);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        using (new AssertionScope())
        {
            dto.Status.Should().Be(OrderStatus.Failed.Name);
            dto.Failure.Should().NotBeNull();
            dto.Failure!.ErrorCode.Should().Be(errorCode);
            dto.Failure.ErrorMessage.Should().Be(errorMessage);
            dto.Failure.AtStatus.Should().Be(OrderStatus.Created.Name);
            // Postgres timestamptz truncates 100-ns precision to microseconds.
            dto.Failure.FailedAtUtc.Should().BeCloseTo(failedAtUtc, TimeSpan.FromSeconds(1));
            dto.Cancellation.Should().BeNull();
            dto.Shipment.Should().BeNull();
        }
    }

    [Fact]
    public async Task Handle_returns_order_with_only_Shipment_populated_when_order_is_shipped()
    {
        var buyerId = Guid.CreateVersion7();
        var ct = TestContext.Current.CancellationToken;

        Order seeded;
        using (var seedScope = _fixture.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(dbContext, TimeProvider.System);
            seeded = await seed.CreateShippedOrderAsync(buyerId: buyerId, cancellationToken: ct);
        }

        var result = await InvokeHandlerAsync(seeded.Id, buyerId, isAdmin: false, ct);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        using (new AssertionScope())
        {
            dto.Status.Should().Be(OrderStatus.Shipped.Name);
            dto.Shipment.Should().NotBeNull();
            dto.Shipment!.Carrier.Should().Be("DHL");
            dto.Shipment.TrackingNumber.Should().Be("1Z999AA10123456784");
            // Round-trip the seeded shipped-at timestamp through the projection.
            // Postgres timestamptz truncates 100-ns precision to microseconds.
            dto.Shipment.ShippedAtUtc.Should().BeCloseTo(seeded.Shipment!.ShippedAtUtc, TimeSpan.FromSeconds(1));
            dto.Cancellation.Should().BeNull();
            dto.Failure.Should().BeNull();
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task Handle_returns_NotFound_when_buyer_requests_another_buyers_order()
    {
        var owner = Guid.CreateVersion7();
        var intruder = Guid.CreateVersion7();
        var ct = TestContext.Current.CancellationToken;

        Order seeded;
        using (var seedScope = _fixture.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(dbContext, TimeProvider.System);
            seeded = await seed.CreateOrderAsync(buyerId: owner, cancellationToken: ct);
        }

        var result = await InvokeHandlerAsync(seeded.Id, intruder, isAdmin: false, ct);

        using (new AssertionScope())
        {
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().ContainSingle(e => e.Message.Contains(seeded.Id.ToString()));
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task Handle_returns_order_for_admin_regardless_of_buyer()
    {
        var owner = Guid.CreateVersion7();
        var admin = Guid.CreateVersion7();
        var ct = TestContext.Current.CancellationToken;

        Order seeded;
        using (var seedScope = _fixture.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(dbContext, TimeProvider.System);
            seeded = await seed.CreateOrderAsync(buyerId: owner, cancellationToken: ct);
        }

        var result = await InvokeHandlerAsync(seeded.Id, admin, isAdmin: true, ct);

        result.IsSuccess.Should().BeTrue();
        using (new AssertionScope())
        {
            result.Value.OrderId.Should().Be(seeded.Id);
            result.Value.BuyerId.Should().Be(owner);
        }
    }

    [Fact]
    public async Task Handle_returns_NotFound_when_order_does_not_exist()
    {
        var missing = Guid.CreateVersion7();
        var ct = TestContext.Current.CancellationToken;

        var result = await InvokeHandlerAsync(missing, Guid.CreateVersion7(), isAdmin: true, ct);

        using (new AssertionScope())
        {
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().ContainSingle(e => e.Message.Contains(missing.ToString()));
        }
    }

    private async Task<FluentResults.Result<GetOrderByIdResponse>> InvokeHandlerAsync(
        Guid orderId,
        Guid buyerId,
        bool isAdmin,
        CancellationToken ct)
    {
        using var queryScope = _fixture.CreateScope();
        var queryContext = queryScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var handler = new GetOrderByIdQueryHandler(
            (IOrderingDbContext)queryContext,
            NullLogger<GetOrderByIdQueryHandler>.Instance);

        return await handler.HandleAsync(
            new GetOrderByIdQuery { OrderId = orderId, BuyerId = buyerId, IsAdmin = isAdmin },
            ct);
    }
}
