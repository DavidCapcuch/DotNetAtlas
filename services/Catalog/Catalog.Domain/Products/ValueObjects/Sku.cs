using System.Text.RegularExpressions;
using Catalog.Domain.Products.Errors;
using FluentResults;
using Platform.SharedKernel.Base;

namespace Catalog.Domain.Products.ValueObjects;

/// <summary>
/// Stock Keeping Unit — business key for a <see cref="Product"/>.
/// Unique across all products, 1–32 chars, alphanumeric + dashes, normalised to uppercase.
/// </summary>
public sealed partial record Sku : ValueObject
{
    public const int MaxLength = 32;

    /// <summary>
    /// The normalised (uppercase) SKU value.
    /// </summary>
    public string Value { get; private init; } = string.Empty;

    private Sku()
    {
    }

    /// <summary>
    /// Creates a validated <see cref="Sku"/>. Input is trimmed and uppercased.
    /// </summary>
    public static Result<Sku> Create(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return Result.Fail(SkuErrors.Empty());
        }

        if (trimmed.Length > MaxLength)
        {
            return Result.Fail(SkuErrors.TooLong(MaxLength));
        }

        if (!SkuPattern().IsMatch(trimmed))
        {
            return Result.Fail(SkuErrors.InvalidCharacters());
        }

        return new Sku { Value = trimmed.ToUpperInvariant() };
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]*$")]
    private static partial Regex SkuPattern();
}
