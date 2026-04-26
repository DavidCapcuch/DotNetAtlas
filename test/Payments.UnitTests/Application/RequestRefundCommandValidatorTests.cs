using Payments.Application.Transactions.RequestRefund;

namespace Payments.UnitTests.Application;

public class RequestRefundCommandValidatorTests
{
    private readonly RequestRefundCommandValidator _validator = new();

    private static RequestRefundCommand Valid() => new()
    {
        PaymentId = Guid.CreateVersion7(),
        CorrelationId = Guid.CreateVersion7(),
        Reason = "saga_compensation",
    };

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyReason_Fails()
    {
        var cmd = Valid() with { Reason = "" };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_TooLongReason_Fails()
    {
        var cmd = Valid() with { Reason = new string('x', 501) };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
