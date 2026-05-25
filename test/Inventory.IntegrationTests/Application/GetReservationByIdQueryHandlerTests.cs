using FluentResults.Extensions.FluentAssertions;
using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.GetReservationById;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Application.StockItems.ReceiveStock;
using Inventory.Application.StockItems.ReserveStock;
using Inventory.Domain.StockItems.ValueObjects;
using Inventory.IntegrationTests.Common;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;
using Platform.SharedKernel.Errors;

namespace Inventory.IntegrationTests.Application;

[Collection<IntegrationTestCollection>]
public sealed class GetReservationByIdQueryHandlerTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 4, 26, 10, 0, 0, TimeSpan.Zero);

    public GetReservationByIdQueryHandlerTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task ExistingReservation_ReturnsAuditRow()
    {
        var productId = Guid.CreateVersion7();
        var reservationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();

        using (var seedScope = Fixture.CreateScope())
        {
            var init = seedScope.ServiceProvider
                .GetRequiredService<ICommandHandler<InitializeStockItemCommand>>();
            var receive = seedScope.ServiceProvider
                .GetRequiredService<ICommandHandler<ReceiveStockCommand, StockLevelResponse>>();
            var reserve = seedScope.ServiceProvider
                .GetRequiredService<ICommandHandler<ReserveStockCommand>>();

            (await init.HandleAsync(
                new InitializeStockItemCommand { ProductId = productId, OccurredOnUtc = UtcNow.AddMinutes(-3) },
                TestContext.Current.CancellationToken)).Should().BeSuccess();
            (await receive.HandleAsync(
                new ReceiveStockCommand
                {
                    ProductId = productId,
                    Quantity = 10,
                    Source = "receiving-dock",
                    ReceivedByUserId = null,
                    OccurredOnUtc = UtcNow.AddMinutes(-2),
                },
                TestContext.Current.CancellationToken)).Should().BeSuccess();
            (await reserve.HandleAsync(
                new ReserveStockCommand
                {
                    ProductId = productId,
                    ReservationId = reservationId,
                    OrderId = orderId,
                    Quantity = 4,
                    TimeToLive = TimeSpan.FromMinutes(15),
                    OccurredOnUtc = UtcNow.AddMinutes(-1),
                },
                TestContext.Current.CancellationToken)).Should().BeSuccess();
        }

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetReservationByIdQuery, ReservationAuditResponse>>();

        var result = await handler.HandleAsync(
            new GetReservationByIdQuery { ReservationId = reservationId },
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        using (new AssertionScope())
        {
            result.Value.ReservationId.Should().Be(reservationId);
            result.Value.ProductId.Should().Be(productId);
            result.Value.OrderId.Should().Be(orderId);
            result.Value.Quantity.Should().Be(4);
            result.Value.Status.Should().Be(ReservationStatus.Active);
            result.Value.ResolvedAtUtc.Should().BeNull();
            result.Value.ReleaseReason.Should().BeNull();
        }
    }

    [Fact]
    public async Task UnknownReservation_ReturnsNotFoundError()
    {
        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetReservationByIdQuery, ReservationAuditResponse>>();

        var result = await handler.HandleAsync(
            new GetReservationByIdQuery { ReservationId = Guid.CreateVersion7() },
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeOfType<NotFoundError>()
            .Which.ErrorCode.Should().Be("Inventory.Reservation.NotFound");
    }
}
