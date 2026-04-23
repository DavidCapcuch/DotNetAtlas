using FluentValidation;

namespace Basket.Application.Baskets.RefreshPrices;

internal sealed class RefreshBasketPricesCommandValidator : AbstractValidator<RefreshBasketPricesCommand>
{
    public RefreshBasketPricesCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
    }
}
