using Payments.Application.Transactions.AuthorizePayment;

namespace Payments.UnitTests.Application;

public class AuthorizePaymentCommandValidatorTests
{
    private readonly AuthorizePaymentCommandValidator _validator = new();

    private static AuthorizePaymentCommand Valid() => new()
    {
        PaymentId = Guid.CreateVersion7(),
        BuyerId = Guid.CreateVersion7(),
        OrderId = Guid.CreateVersion7(),
        Amount = 100m,
        Currency = "USD",
        PaymentMethodId = "tok_visa_4242",
        IdempotencyKey = "saga-key-1",
    };

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        // Arrange & Act & Assert
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveAmount_Fails(decimal amount)
    {
        // Arrange
        var cmd = Valid() with { Amount = amount };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    public void Validate_BadCurrency_Fails(string currency)
    {
        // Arrange
        var cmd = Valid() with { Currency = currency };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "boundary")]
    public void Validate_TooLongPaymentMethod_Fails()
    {
        // Arrange
        var cmd = Valid() with { PaymentMethodId = new string('x', 65) };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyPaymentId_Fails()
    {
        // Arrange
        var cmd = Valid() with { PaymentId = Guid.Empty };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyIdempotencyKey_Fails()
    {
        // Arrange
        var cmd = Valid() with { IdempotencyKey = string.Empty };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
