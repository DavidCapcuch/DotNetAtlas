using Inventory.Application.Common.ReadModels;
using Inventory.Application.StockItems.Common;
using Inventory.Domain.StockItems.ValueObjects;

namespace Inventory.UnitTests.StockItems.Common;

public sealed class ReservationAuditResponseMapperTests
{
    [Fact]
    public void ToReservationAuditResponse_PreservesEveryField()
    {
        var reservationId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var reservedAt = new DateTimeOffset(2026, 4, 26, 9, 0, 0, TimeSpan.Zero);
        var row = new ReservationAuditRow
        {
            ReservationId = reservationId,
            ProductId = productId,
            OrderId = orderId,
            Quantity = 4,
            Status = ReservationStatus.Released,
            ReservedAtUtc = reservedAt,
            ExpiresAtUtc = reservedAt.AddMinutes(15),
            ResolvedAtUtc = reservedAt.AddMinutes(2),
            ReleaseReason = ReleaseReason.Cancellation,
        };

        var response = row.ToReservationAuditResponse();

        using (new AssertionScope())
        {
            response.ReservationId.Should().Be(reservationId);
            response.ProductId.Should().Be(productId);
            response.OrderId.Should().Be(orderId);
            response.Quantity.Should().Be(4);
            response.Status.Should().Be(ReservationStatus.Released);
            response.ReservedAtUtc.Should().Be(reservedAt);
            response.ExpiresAtUtc.Should().Be(reservedAt.AddMinutes(15));
            response.ResolvedAtUtc.Should().Be(reservedAt.AddMinutes(2));
            response.ReleaseReason.Should().Be(ReleaseReason.Cancellation);
        }
    }

    [Fact]
    public void ToReservationAuditResponse_PassesThroughNullableTerminalFields()
    {
        var reservedAt = new DateTimeOffset(2026, 4, 26, 9, 0, 0, TimeSpan.Zero);
        var row = new ReservationAuditRow
        {
            ReservationId = Guid.CreateVersion7(),
            ProductId = Guid.CreateVersion7(),
            OrderId = Guid.CreateVersion7(),
            Quantity = 1,
            Status = ReservationStatus.Active,
            ReservedAtUtc = reservedAt,
            ExpiresAtUtc = reservedAt.AddMinutes(15),
            ResolvedAtUtc = null,
            ReleaseReason = null,
        };

        var response = row.ToReservationAuditResponse();

        response.ResolvedAtUtc.Should().BeNull();
        response.ReleaseReason.Should().BeNull();
    }
}
