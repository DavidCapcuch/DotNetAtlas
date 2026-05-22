using Catalog.Application.Common.Validation;
using FluentValidation;

namespace Catalog.Application.Products.DiscontinueProduct;

public class DiscontinueProductCommandValidator : AbstractValidator<DiscontinueProductCommand>
{
    public DiscontinueProductCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();

        RuleFor(x => x.Reason)
            .NotEmpty()
            .MaximumRuneLength(500);
    }
}
