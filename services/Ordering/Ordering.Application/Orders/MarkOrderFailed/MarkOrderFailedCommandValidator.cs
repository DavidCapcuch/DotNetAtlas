using FluentValidation;

namespace Ordering.Application.Orders.MarkOrderFailed;

public sealed class MarkOrderFailedCommandValidator : AbstractValidator<MarkOrderFailedCommand>
{
    public MarkOrderFailedCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.ErrorCode).NotEmpty().MaximumLength(100);
        RuleFor(c => c.ErrorMessage).NotEmpty().MaximumLength(1000);
    }
}
