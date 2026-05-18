using Catalog.Application.Common.Validation;
using FluentValidation;

namespace Catalog.Application.Products.CreateProduct;

/// <summary>
/// Input-validation rules for <see cref="CreateProductCommand"/> per
/// <c>docs/bc-design/use-cases.md § 1.1.1</c>. Per-value-object constructors in
/// <see cref="Catalog.Domain.Products.Product.Create"/> repeat the rules that guard
/// domain invariants; this validator only shapes the API surface.
/// </summary>
public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Sku)
            .NotEmpty()
            .Length(1, 32)
            .Matches("^[A-Za-z0-9][A-Za-z0-9-]*$");

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Description)
            .NotNull()
            .MaximumLength(4000)
            .Must(value => !HtmlHeuristic.ContainsMarkup(value))
            .WithMessage("Description must not contain HTML markup.");

        RuleFor(x => x.CategoryId).NotEmpty();

        RuleFor(x => x.Brand)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Price)
            .NotNull()
            .ChildRules(price =>
            {
                price.RuleFor(p => p.Amount).GreaterThan(0m);
                price.RuleFor(p => p.Currency)
                    .NotEmpty()
                    .Matches("^[A-Z]{3}$");
            });

        When(x => x.Dimensions is not null, () =>
        {
            RuleFor(x => x.Dimensions!).ChildRules(dim =>
            {
                dim.RuleFor(d => d.Length).GreaterThan(0m);
                dim.RuleFor(d => d.Width).GreaterThan(0m);
                dim.RuleFor(d => d.Height).GreaterThan(0m);
                dim.RuleFor(d => d.Unit)
                    .NotEmpty()
                    .Must(u => u is "cm" or "mm" or "in")
                    .WithMessage("Unit must be one of cm, mm, in.");
            });
        });

        RuleFor(x => x.Images).NotNull();

        RuleForEach(x => x.Images).ChildRules(img =>
        {
            img.RuleFor(i => i.Url).NotEmpty();
            img.RuleFor(i => i.Url)
                .Must(IsHttpOrHttpsAbsoluteUri)
                .When(i => !string.IsNullOrEmpty(i.Url))
                .WithMessage("Image URL must be an absolute http or https URI.");
            img.RuleFor(i => i.AltText)
                .NotEmpty()
                .MaximumLength(200);
            img.RuleFor(i => i.DisplayOrder).GreaterThanOrEqualTo(0);
        });

        RuleFor(x => x.Images)
            .Must(images => images.Select(i => i.DisplayOrder).Distinct().Count() == images.Count)
            .When(x => x.Images is { Count: > 1 })
            .WithMessage("Image DisplayOrder values must be unique within the product.");
    }

    // CAT-SEC-005 (Wave-1 closeout): mirror the domain-level scheme allow-list at the API
    // surface so a hostile URL is rejected before the command reaches Product.Create.
    private static bool IsHttpOrHttpsAbsoluteUri(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
