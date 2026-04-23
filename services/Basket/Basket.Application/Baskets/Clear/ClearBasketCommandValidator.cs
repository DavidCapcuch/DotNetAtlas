using FluentValidation;

namespace Basket.Application.Baskets.Clear;

internal sealed class ClearBasketCommandValidator : AbstractValidator<ClearBasketCommand>
{
    public ClearBasketCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
    }
}
