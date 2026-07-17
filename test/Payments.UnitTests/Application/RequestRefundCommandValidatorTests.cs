using Payments.Application.Transactions.RequestRefund;

namespace Payments.UnitTests.Application;

public class RequestRefundCommandValidatorTests
{
    private readonly RequestRefundCommandValidator _validator = new();

    private static RequestRefundCommand Valid() => new()
    {
        PaymentId = Guid.CreateVersion7(),
        Reason = "saga_compensation",
    };

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        // Arrange & Act & Assert
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyReason_Fails()
    {
        // Arrange
        var cmd = Valid() with { Reason = "" };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_TooLongReason_Fails()
    {
        // Arrange
        var cmd = Valid() with { Reason = new string('x', 501) };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
