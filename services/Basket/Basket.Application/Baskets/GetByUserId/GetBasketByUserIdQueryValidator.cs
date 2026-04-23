using FluentValidation;

namespace Basket.Application.Baskets.GetByUserId;

internal sealed class GetBasketByUserIdQueryValidator : AbstractValidator<GetBasketByUserIdQuery>
{
    public GetBasketByUserIdQueryValidator()
    {
        RuleFor(q => q.UserId).NotEmpty();
    }
}
