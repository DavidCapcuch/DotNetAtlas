using System.Text.RegularExpressions;
using Catalog.Domain.Products.Errors;
using FluentResults;
using Platform.SharedKernel.Base;

namespace Catalog.Domain.Products.ValueObjects;

/// <summary>
/// Display name of a product. Non-empty after trimming, max 200 characters,
/// and internal whitespace is collapsed to single spaces on construction.
/// </summary>
public sealed partial record ProductName : ValueObject
{
    public const int MaxLength = 200;

    public string Value { get; private init; } = string.Empty;

    private ProductName()
    {
    }

    public static Result<ProductName> Create(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return Result.Fail(ProductNameErrors.Empty());
        }

        var collapsed = WhitespacePattern().Replace(trimmed, " ");
        if (collapsed.Length > MaxLength)
        {
            return Result.Fail(ProductNameErrors.TooLong(MaxLength));
        }

        return new ProductName { Value = collapsed };
    }

    public override string ToString() => Value;

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespacePattern();
}
