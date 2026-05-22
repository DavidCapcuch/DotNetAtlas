using FluentValidation.TestHelper;
using Ordering.Application.Orders.MarkOrderDelivered;

namespace Ordering.UnitTests.Application.Orders.MarkOrderDelivered;

public class MarkOrderDeliveredCommandValidatorTests
{
    private readonly MarkOrderDeliveredCommandValidator _validator = new();

    [Fact]
    public void Validate_Happy_HasNoErrors()
    {
        var c = new MarkOrderDeliveredCommand
        {
            OrderId = Guid.CreateVersion7(),
        };
        _validator.TestValidate(c).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyOrderId_Fails()
    {
        var c = new MarkOrderDeliveredCommand
        {
            OrderId = Guid.Empty,
        };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.OrderId);
    }
}
