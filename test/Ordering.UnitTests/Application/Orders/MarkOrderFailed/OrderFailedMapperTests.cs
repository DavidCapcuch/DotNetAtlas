using Ordering.Application.Orders.MarkOrderFailed;
using Ordering.Domain.Orders;
using Ordering.Orders;
using Platform.SharedKernel.Exceptions;

namespace Ordering.UnitTests.Application.Orders.MarkOrderFailed;

/// <summary>
/// Pins <see cref="OrderFailedMapper.MapStatus"/> against the locked
/// <see cref="OrderStatus"/> FSM. <c>Confirmed → Failed</c> is forbidden
/// at the FSM level (<c>OrderStatus.cs</c> transition table), so the
/// mapper must NOT silently accept <c>"Confirmed"</c> as a valid
/// <c>AtStatus</c> for <c>OrderFailedEvent</c>.
/// </summary>
public class OrderFailedMapperTests
{
    [Theory]
    [InlineData("Created", OrderStatusAtTransition.Created)]
    [InlineData("StockReserved", OrderStatusAtTransition.StockReserved)]
    [InlineData("PaymentCompleted", OrderStatusAtTransition.PaymentCompleted)]
    public void MapStatus_ValidPreFailureStatus_ReturnsMatchingAtTransition(
        string name,
        OrderStatusAtTransition expected)
    {
        var actual = OrderFailedMapper.MapStatus(name);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void MapStatus_Confirmed_ThrowsBecauseConfirmedToFailedIsFsmForbidden()
    {
        var ex = Assert.Throws<DataIntegrityException>(() => OrderFailedMapper.MapStatus("Confirmed"));

        Assert.Equal("Order.InvalidAtStatusForFailure", ex.ErrorCode);
    }

    [Theory]
    [InlineData("Shipped")]
    [InlineData("Delivered")]
    [InlineData("Cancelled")]
    [InlineData("Failed")]
    [InlineData("Unknown")]
    public void MapStatus_UnreachableOrUnknown_Throws(string name)
    {
        Assert.Throws<DataIntegrityException>(() => OrderFailedMapper.MapStatus(name));
    }
}
