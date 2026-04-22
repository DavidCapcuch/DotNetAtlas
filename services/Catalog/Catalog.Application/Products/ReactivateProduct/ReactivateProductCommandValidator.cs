using FluentValidation;

namespace Catalog.Application.Products.ReactivateProduct;

public class ReactivateProductCommandValidator : AbstractValidator<ReactivateProductCommand>
{
    public ReactivateProductCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();

        RuleFor(x => x.AdminReactivation)
            .Equal(true)
            .WithMessage("Reactivating a discontinued product requires the admin reactivation flag.");
    }
}
