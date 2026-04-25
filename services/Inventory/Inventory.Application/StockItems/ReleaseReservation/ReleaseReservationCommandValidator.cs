using FluentValidation;

namespace Inventory.Application.StockItems.ReleaseReservation;

public sealed class ReleaseReservationCommandValidator : AbstractValidator<ReleaseReservationCommand>
{
    public ReleaseReservationCommandValidator()
    {
        RuleFor(c => c.ReservationId).NotEmpty();
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.Reason).IsInEnum();
    }
}
