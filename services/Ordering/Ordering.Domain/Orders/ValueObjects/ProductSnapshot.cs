using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;

namespace Ordering.Domain.Orders.ValueObjects;

/// <summary>
/// Frozen, order-time capture of a product. Duplicated per bounded context —
/// the Basket's snapshot has a different shape (image URL for display).
/// Ordering keeps only what appears on the order record itself.
/// </summary>
public sealed record ProductSnapshot : ValueObject
{
    public const int MaxSkuLength = 64;
    public const int MaxNameLength = 200;

    public string Sku { get; private init; } = string.Empty;
    public string Name { get; private init; } = string.Empty;

    private ProductSnapshot()
    {
    }

    public static Result<ProductSnapshot> Create(string? sku, string? name)
    {
        var trimmedSku = sku?.Trim() ?? string.Empty;
        if (trimmedSku.Length == 0)
        {
            return Result.Fail(new ValidationError(
                nameof(Sku), "Product SKU must not be empty.", "ProductSnapshot.SkuEmpty"));
        }

        if (trimmedSku.Length > MaxSkuLength)
        {
            return Result.Fail(new ValidationError(
                nameof(Sku),
                $"Product SKU must not exceed {MaxSkuLength} characters.",
                "ProductSnapshot.SkuTooLong"));
        }

        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0)
        {
            return Result.Fail(new ValidationError(
                nameof(Name), "Product name must not be empty.", "ProductSnapshot.NameEmpty"));
        }

        if (trimmedName.Length > MaxNameLength)
        {
            return Result.Fail(new ValidationError(
                nameof(Name),
                $"Product name must not exceed {MaxNameLength} characters.",
                "ProductSnapshot.NameTooLong"));
        }

        return new ProductSnapshot
        {
            Sku = trimmedSku,
            Name = trimmedName,
        };
    }
}
