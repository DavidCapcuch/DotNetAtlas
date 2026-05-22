using FluentValidation;

namespace Ordering.Application.Orders.GetOrderById;

public sealed class GetOrderByIdQueryValidator : AbstractValidator<GetOrderByIdQuery>
{
    public GetOrderByIdQueryValidator()
    {
        RuleFor(q => q.OrderId).NotEmpty();

        // Buyer reads require a real buyer id (the handler's ownership check
        // relies on it). The admin path deliberately sets BuyerId = Guid.Empty
        // when the JWT sub is not a parseable Guid (service-account tokens) —
        // symmetric with CancelOrderCommandValidator. Guarding NotEmpty
        // unconditionally would block the admin HTTP endpoint.
        RuleFor(q => q.BuyerId)
            .NotEmpty()
            .When(q => !q.IsAdmin);
    }
}
