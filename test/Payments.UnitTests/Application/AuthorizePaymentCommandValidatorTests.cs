using Payments.Application.Transactions.AuthorizePayment;

namespace Payments.UnitTests.Application;

public class AuthorizePaymentCommandValidatorTests
{
    private readonly AuthorizePaymentCommandValidator _validator = new();

    private static AuthorizePaymentCommand Valid() => new()
    {
        PaymentId = Guid.CreateVersion7(),
        CorrelationId = Guid.CreateVersion7(),
        BuyerId = Guid.CreateVersion7(),
        OrderId = Guid.CreateVersion7(),
        Amount = 100m,
        Currency = "USD",
        PaymentMethodId = "tok_visa_4242",
    };

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveAmount_Fails(decimal amount)
    {
        var cmd = Valid() with { Amount = amount };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    public void Validate_BadCurrency_Fails(string currency)
    {
        var cmd = Valid() with { Currency = currency };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_TooLongPaymentMethod_Fails()
    {
        var cmd = Valid() with { PaymentMethodId = new string('x', 65) };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyPaymentId_Fails()
    {
        var cmd = Valid() with { PaymentId = Guid.Empty };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
