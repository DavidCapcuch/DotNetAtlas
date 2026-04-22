using Catalog.Domain.Products.Errors;
using FluentResults;
using Platform.SharedKernel.Base;

namespace Catalog.Domain.Products.ValueObjects;

/// <summary>
/// Brand label attached to a <see cref="Product"/>. Non-empty after trimming, max 100 characters.
/// In v1 brands are not their own aggregate.
/// </summary>
public sealed record BrandName : ValueObject
{
    public const int MaxLength = 100;

    public string Value { get; private init; } = string.Empty;

    private BrandName()
    {
    }

    public static Result<BrandName> Create(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return Result.Fail(BrandNameErrors.Empty());
        }

        if (trimmed.Length > MaxLength)
        {
            return Result.Fail(BrandNameErrors.TooLong(MaxLength));
        }

        return new BrandName { Value = trimmed };
    }

    public override string ToString() => Value;
}
