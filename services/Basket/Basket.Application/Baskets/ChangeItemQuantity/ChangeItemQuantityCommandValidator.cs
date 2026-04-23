using FluentValidation;

namespace Basket.Application.Baskets.ChangeItemQuantity;

internal sealed class ChangeItemQuantityCommandValidator : AbstractValidator<ChangeItemQuantityCommand>
{
    public ChangeItemQuantityCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.NewQuantity).InclusiveBetween(1, 1000);
    }
}
