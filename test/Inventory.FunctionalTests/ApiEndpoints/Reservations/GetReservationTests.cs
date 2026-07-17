using System.Net;
using System.Net.Http.Json;
using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Application.StockItems.ReceiveStock;
using Inventory.Application.StockItems.ReserveStock;
using Inventory.Domain.StockItems.ValueObjects;
using Inventory.FunctionalTests.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.FunctionalTests.ApiEndpoints.Reservations;

/// <summary>
/// End-to-end coverage for <c>GET /api/v1/inventory/reservations/{reservationId}</c> — the
/// reservation-audit lookup. <c>AdminReadPolicy</c> (use-cases.md § 4.4.3 / inventory.md § 9.2):
/// these rows correlate a reservation to an <c>OrderId</c> (internal ops/audit data, not
/// shopper-facing), so the read is gated on the <c>admin</c> role AND a read-capable scope —
/// tighter than the public stock-availability display reads. A plain <c>inventory.read</c>
/// caller is forbidden; only an admin token succeeds.
/// </summary>
[Collection<FunctionalTestCollection>]
public sealed class GetReservationTests : BaseApiTest
{
    public GetReservationTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenAnonymous_Returns401()
    {
        var reservationId = Guid.CreateVersion7();

        var response = await Fixture.HttpClientRegistry.NonAuthClient
            .GetAsync($"/api/v1/inventory/reservations/{reservationId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenReadOnlyScope_WithoutAdminRole_Returns403()
    {
        // inventory.read alone no longer reaches reservation-audit data — the admin-role half
        // of AdminReadPolicy gates it (least privilege over the OrderId-bearing rows).
        var reservationId = Guid.CreateVersion7();

        var response = await Fixture.HttpClientRegistry.ReadOnlyClient
            .GetAsync($"/api/v1/inventory/reservations/{reservationId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenWriteScope_WithoutAdminRole_Returns403()
    {
        // Proves the gate is the role, not the scope: a write-scoped token without the admin
        // role is still forbidden (defense-in-depth, mirrors WritePolicy).
        var reservationId = Guid.CreateVersion7();

        var response = await Fixture.HttpClientRegistry.WriteScopeNoAdminClient
            .GetAsync($"/api/v1/inventory/reservations/{reservationId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WhenAdmin_AndReservationMissing_Returns404()
    {
        var reservationId = Guid.CreateVersion7();

        var response = await Fixture.HttpClientRegistry.CommandsClient
            .GetAsync($"/api/v1/inventory/reservations/{reservationId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WhenAdmin_AndReservationExists_Returns200()
    {
        var productId = Guid.CreateVersion7();
        var reservationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        await SeedActiveReservationAsync(productId, reservationId, orderId);

        var response = await Fixture.HttpClientRegistry.CommandsClient
            .GetAsync($"/api/v1/inventory/reservations/{reservationId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var audit = await response.Content.ReadFromJsonAsync<ReservationAuditResponse>(TestContext.Current.CancellationToken);
        audit.Should().NotBeNull();
        using (new AssertionScope())
        {
            audit!.ReservationId.Should().Be(reservationId);
            audit.ProductId.Should().Be(productId);
            audit.OrderId.Should().Be(orderId);
            audit.Status.Should().Be(ReservationStatus.Active);
        }
    }

    private async Task SeedActiveReservationAsync(Guid productId, Guid reservationId, Guid orderId)
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var init = scope.ServiceProvider.GetRequiredService<Platform.CQRS.ICommandHandler<InitializeStockItemCommand>>();
        var receive = scope.ServiceProvider.GetRequiredService<Platform.CQRS.ICommandHandler<ReceiveStockCommand, StockLevelResponse>>();
        var reserve = scope.ServiceProvider.GetRequiredService<Platform.CQRS.ICommandHandler<ReserveStockCommand>>();

        var now = DateTimeOffset.UtcNow;
        (await init.HandleAsync(
            new InitializeStockItemCommand { ProductId = productId, OccurredOnUtc = now.AddMinutes(-3) },
            TestContext.Current.CancellationToken)).IsSuccess.Should().BeTrue();
        (await receive.HandleAsync(
            new ReceiveStockCommand
            {
                ProductId = productId,
                Quantity = 10,
                Source = "receiving-dock",
                ReceivedByUserId = null,
                OccurredOnUtc = now.AddMinutes(-2),
            },
            TestContext.Current.CancellationToken)).IsSuccess.Should().BeTrue();
        (await reserve.HandleAsync(
            new ReserveStockCommand
            {
                ProductId = productId,
                ReservationId = reservationId,
                OrderId = orderId,
                Quantity = 4,
                TimeToLive = TimeSpan.FromMinutes(15),
                OccurredOnUtc = now.AddMinutes(-1),
            },
            TestContext.Current.CancellationToken)).IsSuccess.Should().BeTrue();
    }
}
