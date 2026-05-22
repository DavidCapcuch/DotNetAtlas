using Catalog.Application.Common.Validation;
using FluentValidation;

namespace Catalog.Application.Categories.CreateCategory;

public class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumRuneLength(100);

        RuleFor(x => x.ParentCategoryId)
            .NotEqual(Guid.Empty)
            .When(x => x.ParentCategoryId.HasValue)
            .WithMessage("ParentCategoryId must be a non-empty Guid when provided.");
    }
}
