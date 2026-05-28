using Payments.Domain.Errors;
using Platform.SharedKernel.Errors;

namespace Payments.UnitTests.Errors;

public class PaymentsErrorsTests
{
    [Fact]
    public void PaymentNotFound_ReturnsNotFoundError()
    {
        var paymentId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var error = PaymentsErrors.PaymentNotFound(paymentId);

        using (new AssertionScope())
        {
            error.Should().BeOfType<NotFoundError>();
            error.EntityName.Should().Be("Payment");
            error.Id.Should().Be(paymentId);
            error.ErrorCode.Should().Be("Payments.NotFound");
            error.Message.Should().Contain(paymentId.ToString());
        }
    }

    [Fact]
    public void InvalidAmount_ExactMessageAndCode()
    {
        var error = PaymentsErrors.InvalidAmount();

        using (new AssertionScope())
        {
            error.Should().BeOfType<ValidationError>();
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
            error.Should().BeOfType<ValidationError>();
            error.PropertyName.Should().Be("PaymentMethodId");
            error.Message.Should().Be("Payment method token is empty or exceeds 64 characters.");
            error.ErrorCode.Should().Be("Payments.InvalidPaymentMethod");
        }
    }

    [Fact]
    public void GatewayUnavailable_ReturnsServiceUnavailableError()
    {
        var error = PaymentsErrors.GatewayUnavailable();

        using (new AssertionScope())
        {
            error.Should().BeOfType<ServiceUnavailableError>();
            error.ResourceName.Should().Be("PaymentGateway");
            error.ErrorCode.Should().Be("Payments.GatewayUnavailable");
            error.Message.Should().Contain("temporarily unavailable");
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
