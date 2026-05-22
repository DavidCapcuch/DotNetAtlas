using FluentValidation.TestHelper;
using Ordering.Application.Orders.MarkOrderStockReserved;

namespace Ordering.UnitTests.Application.Orders.MarkOrderStockReserved;

public class MarkOrderStockReservedCommandValidatorTests
{
    private readonly MarkOrderStockReservedCommandValidator _validator = new();

    [Fact]
    public void Validate_Happy_HasNoErrors()
    {
        var c = new MarkOrderStockReservedCommand
        {
            OrderId = Guid.CreateVersion7(),
            ReservationId = Guid.CreateVersion7(),
        };
        _validator.TestValidate(c).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyOrderId_Fails()
    {
        var c = new MarkOrderStockReservedCommand
        {
            OrderId = Guid.Empty,
            ReservationId = Guid.CreateVersion7(),
        };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.OrderId);
    }

    [Fact]
    public void Validate_EmptyReservationId_Fails()
    {
        var c = new MarkOrderStockReservedCommand
        {
            OrderId = Guid.CreateVersion7(),
            ReservationId = Guid.Empty,
        };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.ReservationId);
    }
}
