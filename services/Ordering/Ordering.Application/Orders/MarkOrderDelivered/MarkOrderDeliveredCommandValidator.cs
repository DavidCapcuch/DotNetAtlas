using FluentValidation;

namespace Ordering.Application.Orders.MarkOrderDelivered;

public sealed class MarkOrderDeliveredCommandValidator : AbstractValidator<MarkOrderDeliveredCommand>
{
    public MarkOrderDeliveredCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
    }
}
