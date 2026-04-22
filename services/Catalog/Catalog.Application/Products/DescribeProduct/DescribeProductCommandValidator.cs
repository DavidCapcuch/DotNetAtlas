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
            .Must(value => !ContainsHtml(value))
            .WithMessage("Description must not contain HTML markup.");
    }

    private static bool ContainsHtml(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        for (var i = 0; i < value.Length - 1; i++)
        {
            if (value[i] == '<' && char.IsLetter(value[i + 1]))
            {
                return true;
            }
        }

        return false;
    }
}
