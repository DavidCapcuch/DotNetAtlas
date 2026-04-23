using FluentValidation;

namespace Ordering.Application.Orders.GetOrderById;

public sealed class GetOrderByIdQueryValidator : AbstractValidator<GetOrderByIdQuery>
{
    public GetOrderByIdQueryValidator()
    {
        RuleFor(q => q.OrderId).NotEmpty();
        RuleFor(q => q.BuyerId).NotEmpty();
    }
}
