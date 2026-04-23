using FluentValidation;

namespace Ordering.Application.Orders.MarkOrderStockReserved;

public sealed class MarkOrderStockReservedCommandValidator : AbstractValidator<MarkOrderStockReservedCommand>
{
    public MarkOrderStockReservedCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.ReservationId).NotEmpty();
    }
}
