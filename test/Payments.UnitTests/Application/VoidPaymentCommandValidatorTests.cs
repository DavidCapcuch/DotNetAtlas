using Payments.Application.Transactions.VoidPayment;

namespace Payments.UnitTests.Application;

public class VoidPaymentCommandValidatorTests
{
    private readonly VoidPaymentCommandValidator _validator = new();

    private static VoidPaymentCommand Valid() => new()
    {
        OrderId = Guid.CreateVersion7(),
        AuthorizationId = "gw-tx-abc",
        Reason = "saga_compensation",
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
        // Arrange & Act & Assert
        _validator.Validate(Valid() with { OrderId = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyAuthorizationId_Fails()
    {
        // Arrange & Act & Assert
        _validator.Validate(Valid() with { AuthorizationId = string.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyReason_Fails()
    {
        // Arrange & Act & Assert
        _validator.Validate(Valid() with { Reason = string.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ReasonTooLong_Fails()
    {
        // Arrange & Act & Assert
        _validator.Validate(Valid() with { Reason = new string('x', 257) }).IsValid.Should().BeFalse();
    }
}
