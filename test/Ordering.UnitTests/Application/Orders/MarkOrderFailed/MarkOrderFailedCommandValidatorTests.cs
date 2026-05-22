using FluentValidation.TestHelper;
using Ordering.Application.Orders.MarkOrderFailed;

namespace Ordering.UnitTests.Application.Orders.MarkOrderFailed;

public class MarkOrderFailedCommandValidatorTests
{
    private readonly MarkOrderFailedCommandValidator _validator = new();

    private static MarkOrderFailedCommand Valid() => new()
    {
        OrderId = Guid.CreateVersion7(),
        ErrorCode = "Saga.PaymentRejected",
        ErrorMessage = "Payment gateway returned 402 Insufficient Funds.",
    };

    [Fact]
    public void Validate_Happy_HasNoErrors()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyOrderId_Fails()
    {
        var c = new MarkOrderFailedCommand
        {
            OrderId = Guid.Empty,
            ErrorCode = "x",
            ErrorMessage = "y",
        };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.OrderId);
    }

    [Fact]
    public void Validate_EmptyErrorCode_Fails()
    {
        var c = new MarkOrderFailedCommand
        {
            OrderId = Guid.CreateVersion7(),
            ErrorCode = string.Empty,
            ErrorMessage = "y",
        };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.ErrorCode);
    }

    [Fact]
    public void Validate_ErrorCodeOver100Chars_Fails()
    {
        var c = new MarkOrderFailedCommand
        {
            OrderId = Guid.CreateVersion7(),
            ErrorCode = new string('x', 101),
            ErrorMessage = "y",
        };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.ErrorCode);
    }

    [Fact]
    public void Validate_ErrorMessageOver1000Chars_Fails()
    {
        var c = new MarkOrderFailedCommand
        {
            OrderId = Guid.CreateVersion7(),
            ErrorCode = "x",
            ErrorMessage = new string('m', 1001),
        };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.ErrorMessage);
    }
}
