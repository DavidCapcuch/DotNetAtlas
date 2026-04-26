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

[Collection<FunctionalTestCollection>]
public sealed class GetReservationTests : BaseApiTest
{
    public GetReservationTests(InventoryApiFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenAnonymous_Returns401()
    {
        var reservationId = Guid.CreateVersion7();

        var response = await Fixture.HttpClientRegistry.NonAuthClient
            .GetAsync($"/api/v1/inventory/reservations/{reservationId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenReadOnlyScope_AndReservationMissing_Returns404()
    {
        var reservationId = Guid.CreateVersion7();

        var response = await Fixture.HttpClientRegistry.ReadOnlyClient
            .GetAsync($"/api/v1/inventory/reservations/{reservationId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WhenReadOnlyScope_AndReservationExists_Returns200()
    {
        var productId = Guid.CreateVersion7();
        var reservationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        await SeedActiveReservationAsync(productId, reservationId, orderId);

        var response = await Fixture.HttpClientRegistry.ReadOnlyClient
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
