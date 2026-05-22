using FluentValidation.TestHelper;
using Ordering.Application.Orders.MarkOrderShipped;

namespace Ordering.UnitTests.Application.Orders.MarkOrderShipped;

public class MarkOrderShippedCommandValidatorTests
{
    private readonly MarkOrderShippedCommandValidator _validator = new();

    private static MarkOrderShippedCommand Valid() => new()
    {
        OrderId = Guid.CreateVersion7(),
        Carrier = "DHL",
        TrackingNumber = "TRK-42",
    };

    [Fact]
    public void Validate_Happy_HasNoErrors()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyOrderId_Fails()
    {
        var c = new MarkOrderShippedCommand
        {
            OrderId = Guid.Empty,
            Carrier = "DHL",
            TrackingNumber = "x",
        };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.OrderId);
    }

    [Fact]
    public void Validate_EmptyCarrier_Fails()
    {
        var c = new MarkOrderShippedCommand
        {
            OrderId = Guid.CreateVersion7(),
            Carrier = string.Empty,
            TrackingNumber = "x",
        };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.Carrier);
    }

    [Fact]
    public void Validate_CarrierOver100Chars_Fails()
    {
        var c = new MarkOrderShippedCommand
        {
            OrderId = Guid.CreateVersion7(),
            Carrier = new string('c', 101),
            TrackingNumber = "x",
        };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.Carrier);
    }

    [Fact]
    public void Validate_EmptyTrackingNumber_Fails()
    {
        var c = new MarkOrderShippedCommand
        {
            OrderId = Guid.CreateVersion7(),
            Carrier = "DHL",
            TrackingNumber = string.Empty,
        };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.TrackingNumber);
    }
}
