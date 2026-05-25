using FluentValidation;
using Ordering.Domain.Orders;

namespace Ordering.Application.Orders.GetOrdersByBuyer;

public sealed class GetOrdersByBuyerQueryValidator : AbstractValidator<GetOrdersByBuyerQuery>
{
    public GetOrdersByBuyerQueryValidator()
    {
        RuleFor(q => q.BuyerId).NotEmpty();
        RuleFor(q => q.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);

        // use-cases.md § 3.4.2 — when provided, Status must parse to a valid
        // OrderStatus SmartEnum name. An invalid value returns 400 rather than
        // silently degrading to "no filter" which would leak unrelated orders
        // back to the caller.
        RuleFor(q => q.Status)
            .Must(s => s is null || OrderStatus.TryFromName(s, out _))
            .When(q => q.Status is not null)
            .WithMessage("Status must be a valid OrderStatus name.");
    }
}
