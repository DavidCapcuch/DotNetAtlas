using FluentValidation;

namespace Catalog.Application.Categories.ReparentCategory;

public class ReparentCategoryCommandValidator : AbstractValidator<ReparentCategoryCommand>
{
    public ReparentCategoryCommandValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();

        RuleFor(x => x.NewParentCategoryId)
            .NotEqual(Guid.Empty)
            .When(x => x.NewParentCategoryId.HasValue)
            .WithMessage("NewParentCategoryId must be a non-empty Guid when provided.");

        RuleFor(x => x.NewParentCategoryId)
            .Must((cmd, parent) => parent != cmd.CategoryId)
            .When(x => x.NewParentCategoryId.HasValue)
            .WithMessage("A category cannot be reparented under itself.");
    }
}
