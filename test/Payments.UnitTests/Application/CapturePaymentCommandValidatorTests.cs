using Payments.Application.Transactions.CapturePayment;

namespace Payments.UnitTests.Application;

public class CapturePaymentCommandValidatorTests
{
    private readonly CapturePaymentCommandValidator _validator = new();

    private static CapturePaymentCommand Valid() => new()
    {
        OrderId = Guid.CreateVersion7(),
        AuthorizationId = "gw-tx-abc",
    };

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        // Arrange & Act & Assert
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyOrderId_Fails()
    {
        // Arrange
        var cmd = Valid() with { OrderId = Guid.Empty };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyAuthorizationId_Fails()
    {
        // Arrange
        var cmd = Valid() with { AuthorizationId = string.Empty };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
