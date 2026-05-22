using FluentValidation.TestHelper;
using Ordering.Application.Orders.MarkOrderPaymentCompleted;

namespace Ordering.UnitTests.Application.Orders.MarkOrderPaymentCompleted;

public class MarkOrderPaymentCompletedCommandValidatorTests
{
    private readonly MarkOrderPaymentCompletedCommandValidator _validator = new();

    [Fact]
    public void Validate_Happy_HasNoErrors()
    {
        var c = new MarkOrderPaymentCompletedCommand
        {
            OrderId = Guid.CreateVersion7(),
            PaymentTransactionId = Guid.CreateVersion7(),
        };
        _validator.TestValidate(c).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyOrderId_Fails()
    {
        var c = new MarkOrderPaymentCompletedCommand
        {
            OrderId = Guid.Empty,
            PaymentTransactionId = Guid.CreateVersion7(),
        };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.OrderId);
    }

    [Fact]
    public void Validate_EmptyPaymentTransactionId_Fails()
    {
        var c = new MarkOrderPaymentCompletedCommand
        {
            OrderId = Guid.CreateVersion7(),
            PaymentTransactionId = Guid.Empty,
        };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.PaymentTransactionId);
    }
}
