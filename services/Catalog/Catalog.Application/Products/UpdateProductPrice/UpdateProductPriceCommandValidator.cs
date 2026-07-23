using FluentValidation;

namespace Catalog.Application.Products.UpdateProductPrice;

public class UpdateProductPriceCommandValidator : AbstractValidator<UpdateProductPriceCommand>
{
    public UpdateProductPriceCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();

        RuleFor(x => x.NewAmount).GreaterThan(0m);
    }
}
