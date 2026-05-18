using Catalog.Application.Common.Validation;
using FluentValidation;

namespace Catalog.Application.Products.DescribeProduct;

public class DescribeProductCommandValidator : AbstractValidator<DescribeProductCommand>
{
    public DescribeProductCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();

        RuleFor(x => x.NewDescription)
            .NotNull()
            .MaximumLength(4000)
            .Must(value => !HtmlHeuristic.ContainsMarkup(value))
            .WithMessage("Description must not contain HTML markup.");
    }
}
