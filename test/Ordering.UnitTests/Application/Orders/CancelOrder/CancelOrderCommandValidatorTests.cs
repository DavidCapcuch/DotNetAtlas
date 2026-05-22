using FluentValidation.TestHelper;
using Ordering.Application.Orders.CancelOrder;

namespace Ordering.UnitTests.Application.Orders.CancelOrder;

/// <summary>
/// Pins the dual-mode authorisation contract on the cancel command: the
/// buyer HTTP path requires <c>BuyerId</c>, but the admin HTTP path and
/// the saga compensation path set <c>BuyerId=Guid.Empty</c> and rely on
/// <c>IsAdmin=true</c>.
/// </summary>
public class CancelOrderCommandValidatorTests
{
    private readonly CancelOrderCommandValidator _validator = new();

    [Fact]
    public void Validate_BuyerHappyPath_HasNoErrors()
    {
        var c = new CancelOrderCommand
        {
            OrderId = Guid.CreateVersion7(),
            Reason = "Changed my mind",
            BuyerId = Guid.CreateVersion7(),
            IsAdmin = false,
        };
        _validator.TestValidate(c).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_AdminWithEmptyBuyerId_HasNoErrors()
    {
        var c = new CancelOrderCommand
        {
            OrderId = Guid.CreateVersion7(),
            Reason = "Admin force-cancel",
            BuyerId = Guid.Empty,
            IsAdmin = true,
        };
        _validator.TestValidate(c).ShouldNotHaveValidationErrorFor(x => x.BuyerId);
    }

    [Fact]
    public void Validate_BuyerWithEmptyBuyerId_Fails()
    {
        var c = new CancelOrderCommand
        {
            OrderId = Guid.CreateVersion7(),
            Reason = "x",
            BuyerId = Guid.Empty,
            IsAdmin = false,
        };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.BuyerId);
    }

    [Fact]
    public void Validate_EmptyOrderId_Fails()
    {
        var c = new CancelOrderCommand
        {
            OrderId = Guid.Empty,
            Reason = "x",
            BuyerId = Guid.CreateVersion7(),
            IsAdmin = false,
        };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.OrderId);
    }

    [Fact]
    public void Validate_EmptyReason_Fails()
    {
        var c = new CancelOrderCommand
        {
            OrderId = Guid.CreateVersion7(),
            Reason = string.Empty,
            BuyerId = Guid.CreateVersion7(),
            IsAdmin = false,
        };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void Validate_ReasonOver500Chars_Fails()
    {
        var c = new CancelOrderCommand
        {
            OrderId = Guid.CreateVersion7(),
            Reason = new string('r', 501),
            BuyerId = Guid.CreateVersion7(),
            IsAdmin = false,
        };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.Reason);
    }
}
