using FluentValidation;

namespace Catalog.Application.Products.ReactivateProduct;

public class ReactivateProductCommandValidator : AbstractValidator<ReactivateProductCommand>
{
    public ReactivateProductCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();

        // AdminReactivation flag is intentionally NOT validated here. The aggregate's
        // Product.Reactivate(...) enforces it and returns ProductErrors.ReactivationRequiresAdminFlag
        // as a ForbiddenError (→ 403). Duplicating the rule as a FluentValidation pre-handler
        // check would force the Platform.CQRS ValidationBehavior to wrap it in a ValidationError
        // (→ 422), shadowing the aggregate's correct ForbiddenError dispatch.
    }
}
