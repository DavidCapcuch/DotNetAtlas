using FluentValidation;

namespace Catalog.Application.Products.GetProductsByIds;

public class GetProductsByIdsQueryValidator : AbstractValidator<GetProductsByIdsQuery>
{
    public GetProductsByIdsQueryValidator()
    {
        RuleFor(x => x.Ids)
            .NotNull()
            .Must(ids => ids.Count is >= 1 and <= 100)
            .WithMessage("Ids must contain between 1 and 100 values.");

        RuleForEach(x => x.Ids).NotEqual(Guid.Empty);
    }
}
