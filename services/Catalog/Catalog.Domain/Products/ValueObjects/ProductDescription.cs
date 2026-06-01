using Catalog.Domain.Products.Errors;
using FluentResults;
using Platform.SharedKernel.Base;

namespace Catalog.Domain.Products.ValueObjects;

/// <summary>
/// Product description. Empty is allowed (a product need not be fully described at creation).
/// Max 4000 characters.
/// </summary>
public sealed record ProductDescription : ValueObject
{
    public const int MaxLength = 4000;

    public string Value { get; private init; } = string.Empty;

    private ProductDescription()
    {
    }

    public static Result<ProductDescription> Create(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > MaxLength)
        {
            return Result.Fail(ProductDescriptionErrors.TooLong(MaxLength));
        }

        return new ProductDescription { Value = text };
    }

    public override string ToString() => Value;
}
