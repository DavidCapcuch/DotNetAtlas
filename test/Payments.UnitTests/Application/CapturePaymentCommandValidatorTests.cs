using Payments.Application.Transactions.CapturePayment;

namespace Payments.UnitTests.Application;

public class CapturePaymentCommandValidatorTests
{
    private readonly CapturePaymentCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        var cmd = new CapturePaymentCommand { PaymentId = Guid.CreateVersion7(), CorrelationId = Guid.CreateVersion7(), AuthorizationId = "gw-tx-abc" };
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyPaymentId_Fails()
    {
        var cmd = new CapturePaymentCommand { PaymentId = Guid.Empty, CorrelationId = Guid.CreateVersion7(), AuthorizationId = "gw-tx-abc" };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyCorrelationId_Fails()
    {
        var cmd = new CapturePaymentCommand { PaymentId = Guid.CreateVersion7(), CorrelationId = Guid.Empty, AuthorizationId = "gw-tx-abc" };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyAuthorizationId_Fails()
    {
        var cmd = new CapturePaymentCommand { PaymentId = Guid.CreateVersion7(), CorrelationId = Guid.CreateVersion7(), AuthorizationId = string.Empty };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
