using FluentValidation;

namespace Catalog.Application.Products.UpdateProductPrice;

public class UpdateProductPriceCommandValidator : AbstractValidator<UpdateProductPriceCommand>
{
    public UpdateProductPriceCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();

        RuleFor(x => x.NewPrice)
            .NotNull()
            .ChildRules(price =>
            {
                price.RuleFor(p => p.Amount).GreaterThan(0m);
                price.RuleFor(p => p.Currency)
                    .NotEmpty()
                    .Matches("^[A-Z]{3}$");
            });
    }
}
