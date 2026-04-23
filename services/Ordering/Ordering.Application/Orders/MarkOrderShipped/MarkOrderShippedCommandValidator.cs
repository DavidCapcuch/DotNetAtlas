using FluentValidation;

namespace Ordering.Application.Orders.MarkOrderShipped;

public sealed class MarkOrderShippedCommandValidator : AbstractValidator<MarkOrderShippedCommand>
{
    public MarkOrderShippedCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.Carrier).NotEmpty().MaximumLength(100);
        RuleFor(c => c.TrackingNumber).NotEmpty().MaximumLength(100);
    }
}
