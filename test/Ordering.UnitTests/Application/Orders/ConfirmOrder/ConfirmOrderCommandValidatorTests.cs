using FluentValidation.TestHelper;
using Ordering.Application.Orders.ConfirmOrder;

namespace Ordering.UnitTests.Application.Orders.ConfirmOrder;

public class ConfirmOrderCommandValidatorTests
{
    private readonly ConfirmOrderCommandValidator _validator = new();

    [Fact]
    public void Validate_Happy_HasNoErrors()
    {
        var c = new ConfirmOrderCommand
        {
            OrderId = Guid.CreateVersion7(),
        };
        _validator.TestValidate(c).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyOrderId_Fails()
    {
        var c = new ConfirmOrderCommand
        {
            OrderId = Guid.Empty,
        };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.OrderId);
    }
}
