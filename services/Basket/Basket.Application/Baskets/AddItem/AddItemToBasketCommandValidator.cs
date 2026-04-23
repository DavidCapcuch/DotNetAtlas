using FluentValidation;

namespace Basket.Application.Baskets.AddItem;

internal sealed class AddItemToBasketCommandValidator : AbstractValidator<AddItemToBasketCommand>
{
    public AddItemToBasketCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.Quantity).InclusiveBetween(1, 1000);
    }
}
