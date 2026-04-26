using FluentValidation;

namespace Inventory.Application.StockItems.GetReservationById;

public sealed class GetReservationByIdQueryValidator : AbstractValidator<GetReservationByIdQuery>
{
    public GetReservationByIdQueryValidator()
    {
        RuleFor(q => q.ReservationId).NotEmpty();
    }
}
