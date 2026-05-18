using Payments.Application.Transactions.VoidPayment;

namespace Payments.UnitTests.Application;

public class VoidPaymentCommandValidatorTests
{
    private readonly VoidPaymentCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        var cmd = new VoidPaymentCommand { PaymentId = Guid.CreateVersion7(), CorrelationId = Guid.CreateVersion7(), AuthorizationId = "gw-tx-abc" };
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyPaymentId_Fails()
    {
        var cmd = new VoidPaymentCommand { PaymentId = Guid.Empty, CorrelationId = Guid.CreateVersion7(), AuthorizationId = "gw-tx-abc" };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyCorrelationId_Fails()
    {
        var cmd = new VoidPaymentCommand { PaymentId = Guid.CreateVersion7(), CorrelationId = Guid.Empty, AuthorizationId = "gw-tx-abc" };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyAuthorizationId_Fails()
    {
        var cmd = new VoidPaymentCommand { PaymentId = Guid.CreateVersion7(), CorrelationId = Guid.CreateVersion7(), AuthorizationId = string.Empty };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
