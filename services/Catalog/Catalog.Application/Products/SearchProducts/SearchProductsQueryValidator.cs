using Catalog.Application.Common.Validation;
using Catalog.Domain.Products.ValueObjects;
using FluentValidation;

namespace Catalog.Application.Products.SearchProducts;

public class SearchProductsQueryValidator : AbstractValidator<SearchProductsQuery>
{
    public SearchProductsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);

        // CAT-SEC-001 / CAT-RV-H03: bound user-supplied free-text search to keep LIKE scans
        // pinned; the handler additionally escapes wildcard metacharacters before substitution.
        // CAT-SEC-006: count runes rather than UTF-16 code units so emoji
        // do not get truncated mid-surrogate.
        RuleFor(x => x.Text)
            .MaximumRuneLength(100)
            .When(x => !string.IsNullOrEmpty(x.Text));

        RuleFor(x => x.MinPrice)
            .GreaterThan(0m)
            .When(x => x.MinPrice.HasValue);

        RuleFor(x => x.MaxPrice)
            .GreaterThanOrEqualTo(x => x.MinPrice!.Value)
            .When(x => x.MinPrice.HasValue && x.MaxPrice.HasValue);

        RuleFor(x => x.Currency)
            .NotEmpty()
            .When(x => x.MinPrice.HasValue || x.MaxPrice.HasValue)
            .WithMessage("Currency is required when a price filter is supplied.");

        RuleFor(x => x.Currency)
            .Matches("^[A-Z]{3}$")
            .When(x => !string.IsNullOrEmpty(x.Currency))
            .WithMessage("Currency must be a three-letter ISO 4217 code.");

        RuleFor(x => x.CategoryPathPrefix)
            .Matches("^(/[a-z0-9][a-z0-9-]*){1,5}$")
            .When(x => !string.IsNullOrEmpty(x.CategoryPathPrefix))
            .WithMessage("CategoryPathPrefix must be a valid materialized path (e.g. /electronics/computers).");

        RuleFor(x => x.Status)
            .Must(status => ProductStatus.TryFromName(status!, ignoreCase: false, out _))
            .When(x => !string.IsNullOrEmpty(x.Status))
            .WithMessage("Status must be one of Active, Discontinued.");
    }
}
