using Catalog.Domain.Products.Errors;
using FluentResults;
using Platform.SharedKernel.Base;

namespace Catalog.Domain.Products.ValueObjects;

/// <summary>
/// Physical dimensions of a product. Present for physical goods; null on the
/// <see cref="Product"/> aggregate for digital/service products. Units are whitelisted.
/// </summary>
public sealed record Dimensions : ValueObject
{
    public static readonly IReadOnlySet<string> SupportedUnits =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cm", "mm", "in" };

    public decimal Length { get; private init; }
    public decimal Width { get; private init; }
    public decimal Height { get; private init; }
    public string Unit { get; private init; } = string.Empty;

    private Dimensions()
    {
    }

    public static Result<Dimensions> Create(decimal length, decimal width, decimal height, string? unit)
    {
        if (length <= 0 || width <= 0 || height <= 0)
        {
            return Result.Fail(DimensionsErrors.NonPositiveDimension());
        }

        var trimmedUnit = unit?.Trim() ?? string.Empty;
        var canonicalUnit = SupportedUnits.FirstOrDefault(
            u => string.Equals(u, trimmedUnit, StringComparison.OrdinalIgnoreCase));
        if (canonicalUnit is null)
        {
            return Result.Fail(DimensionsErrors.UnsupportedUnit());
        }

        return new Dimensions
        {
            Length = length,
            Width = width,
            Height = height,
            Unit = canonicalUnit
        };
    }
}
