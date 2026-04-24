using Payments.Domain.Errors;

namespace Payments.UnitTests.Errors;

public class PaymentsErrorsTests
{
    [Fact]
    public void PaymentNotFound_ExactMessageAndCode()
    {
        var paymentId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var error = PaymentsErrors.PaymentNotFound(paymentId);

        using (new AssertionScope())
        {
            error.PropertyName.Should().Be("PaymentId");
            error.Message.Should().Be($"Payment '{paymentId}' does not exist.");
            error.ErrorCode.Should().Be("Payments.NotFound");
        }
    }

    [Fact]
    public void InvalidAmount_ExactMessageAndCode()
    {
        var error = PaymentsErrors.InvalidAmount();

        using (new AssertionScope())
        {
            error.PropertyName.Should().Be("Amount");
            error.Message.Should().Be("Payment amount must be strictly positive.");
            error.ErrorCode.Should().Be("Payments.InvalidAmount");
        }
    }

    [Fact]
    public void InvalidPaymentMethod_ExactMessageAndCode()
    {
        var error = PaymentsErrors.InvalidPaymentMethod();

        using (new AssertionScope())
        {
            error.PropertyName.Should().Be("PaymentMethodId");
            error.Message.Should().Be("Payment method token is empty or exceeds 64 characters.");
            error.ErrorCode.Should().Be("Payments.InvalidPaymentMethod");
        }
    }

    [Fact]
    public void GatewayUnavailable_ExactMessageAndCode()
    {
        var error = PaymentsErrors.GatewayUnavailable();

        using (new AssertionScope())
        {
            error.PropertyName.Should().Be("Gateway");
            error.Message.Should().Be("Payment gateway is temporarily unavailable.");
            error.ErrorCode.Should().Be("Payments.GatewayUnavailable");
        }
    }

    [Fact]
    public void GatewayDeclinedError_WithGatewayCode_FormatsMessage()
    {
        var error = new GatewayDeclinedError("insufficient_funds", "insufficient_funds");

        using (new AssertionScope())
        {
            error.Message.Should().Be("Payment gateway declined: insufficient_funds (insufficient_funds).");
            error.Metadata["ErrorCode"].Should().Be("Payments.GatewayDeclined");
        }
    }

    [Fact]
    public void GatewayDeclinedError_WithoutGatewayCode_FormatsMessage()
    {
        var error = new GatewayDeclinedError("insufficient_funds", GatewayCode: null);

        using (new AssertionScope())
        {
            error.Message.Should().Be("Payment gateway declined: insufficient_funds");
            error.Metadata["ErrorCode"].Should().Be("Payments.GatewayDeclined");
        }
    }
}
