using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Data;
using Ordering.Application.Orders.CreateOrder;
using Ordering.Domain.Baskets;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Persistence.Database;
using Ordering.IntegrationTests.Common;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.IntegrationTests.Persistence;

/// <summary>
/// Smoke test: proves the Infrastructure layer can round-trip an
/// <see cref="Order"/> aggregate through Postgres — OrderingDbContext,
/// EF mappings (including <c>_enc</c> PII columns + owned Money + owned
/// OrderItem collection), SmartEnum conversion, and the
/// <c>DispatchDomainEventsInterceptor</c> all participate.
/// Full Kafka saga-command ingress coverage lands.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class OrderPersistenceTests
{
    private readonly IntegrationTestFixture _fixture;

    public OrderPersistenceTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Given_valid_basket_When_Order_created_and_saved_Then_round_trips_all_owned_types()
    {
        using var scope = _fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        var orderId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();

        var basketItems = new[]
        {
            new BasketSnapshotItem(
                ProductId: productId,
                Sku: "SKU-42",
                Name: "Acme Widget",
                Quantity: 2,
                UnitPriceAmount: 19.99m),
        };

        var basket = new BasketSnapshot(buyerId, CurrencyCode.Eur, basketItems);

        var shipping = Address.Create("221B Baker Street", null, "London", null, "NW1 6XE", "GB").Value;
        var billing = Address.Create("10 Downing Street", null, "London", null, "SW1A 2AA", "GB").Value;

        // Snapshot "now" before the act phase so the audit-interceptor assertion
        // below has a wall-clock-stable lower bound (the interceptor calls
        // TimeProvider.System.GetUtcNow() during SaveChangesAsync).
        var nowSnapshot = DateTimeOffset.UtcNow;

        var order = Order.CreateFromBasket(
            orderId,
            buyerId,
            basket,
            shipping,
            billing,
            paymentMethodId,
            nowSnapshot);

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Fresh scope to force a real DB read (bypass the change tracker).
        using var readScope = _fixture.CreateScope();
        var readContext = readScope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        // PK lookup (ADR-0022): inline LINQ, no spec. The owned OrderItem collection
        // auto-loads with the root, so no .Include is needed to assert the round-trip below.
        var loaded = await readContext.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == order.Id, TestContext.Current.CancellationToken);

        loaded.Should().NotBeNull();
        loaded!.BuyerId.Should().Be(buyerId);
        loaded.PaymentMethodId.Should().Be(paymentMethodId);
        loaded.Status.Should().Be(OrderStatus.Created);
        loaded.Total.Amount.Should().Be(39.98m);
        loaded.Total.Currency.Should().Be(CurrencyCode.Eur);

        loaded.ShippingAddress.Street1.Should().Be("221B Baker Street");
        loaded.ShippingAddress.CountryCode.Should().Be("GB");
        loaded.BillingAddress.PostalCode.Should().Be("SW1A 2AA");

        loaded.Items.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                ProductId = productId,
                Quantity = 2,
                UnitPrice = new { Amount = 19.99m, Currency = CurrencyCode.Eur },
                LineTotal = new { Amount = 39.98m, Currency = CurrencyCode.Eur },
                ProductSnapshot = new { Sku = "SKU-42", Name = "Acme Widget" },
            });

        // Audit-interceptor stamps come from TimeProvider.System; allow a
        // small wall-clock slack to absorb the interceptor's clock read vs
        // the snapshot captured before the act phase.
        loaded.CreatedUtc.Should().BeCloseTo(nowSnapshot, TimeSpan.FromSeconds(5));
        loaded.LastModifiedUtc.Should().BeCloseTo(nowSnapshot, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Exercises the <see cref="CreateOrderCommandHandler"/> idempotency branch
    /// against the real Postgres-backed <see cref="OrderingDbContext"/> — a
    /// Kafka redelivery (or saga retry) of the same <c>OrderId</c> (the
    /// client-assigned aggregate PK per ADR-0029) must return the original
    /// <c>OrderId</c> without a duplicate insert (the pre-check short-circuits
    /// before the PK-violation insert).
    /// </summary>
    [Fact]
    public async Task Given_duplicate_OrderId_When_handler_invoked_twice_Then_second_returns_existing_OrderId()
    {
        using var scope = _fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<CreateOrderCommandHandler>>();
        var handler = new CreateOrderCommandHandler(
            (IOrderingDbContext)dbContext,
            logger);

        var command = new CreateOrderCommand
        {
            OrderId = Guid.CreateVersion7(),
            BuyerId = Guid.CreateVersion7(),
            PaymentMethodId = Guid.CreateVersion7(),
            Currency = CurrencyCode.Usd.Name,
            Items =
            [
                new CreateOrderItemInput(
                    ProductId: Guid.CreateVersion7(),
                    Sku: "IDEMPOTENT-SKU",
                    Name: "Idempotent widget",
                    Quantity: 1,
                    UnitPriceAmount: 42m),
            ],
            ShippingAddress = new AddressInput(
                "1 Idempotent Way", null, "Palo Alto", "CA", "94301", "US"),
            BillingAddress = new AddressInput(
                "1 Idempotent Way", null, "Palo Alto", "CA", "94301", "US"),
            RequestedAtUtc = DateTimeOffset.UtcNow,
        };

        var first = await handler.HandleAsync(command, TestContext.Current.CancellationToken);
        first.IsSuccess.Should().BeTrue();

        // Second dispatch with the same OrderId — handler's pre-check
        // should short-circuit and return the same OrderId, NOT throw DbUpdateException.
        var second = await handler.HandleAsync(command, TestContext.Current.CancellationToken);
        second.IsSuccess.Should().BeTrue();
        second.Value.Should().Be(first.Value, "replay must be idempotent on OrderId");

        // And only ONE row actually persisted.
        using var verifyScope = _fixture.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var count = await verifyContext.Orders
            .AsNoTracking()
            .CountAsync(o => o.Id == command.OrderId, TestContext.Current.CancellationToken);
        count.Should().Be(1);
    }
}
