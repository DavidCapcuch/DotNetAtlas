using FluentValidation;

namespace Inventory.Application.StockItems.ConfirmReservation;

public sealed class ConfirmReservationCommandValidator : AbstractValidator<ConfirmReservationCommand>
{
    public ConfirmReservationCommandValidator()
    {
        RuleFor(c => c.ReservationId).NotEmpty();
        RuleFor(c => c.ProductId).NotEmpty();
    }
}
