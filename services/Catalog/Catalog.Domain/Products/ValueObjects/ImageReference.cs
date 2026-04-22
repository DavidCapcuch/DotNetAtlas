using Catalog.Domain.Products.Errors;
using FluentResults;
using Platform.SharedKernel.Base;

namespace Catalog.Domain.Products.ValueObjects;

/// <summary>
/// A product image reference — absolute URL + alt text + display order.
/// Stored in a <see cref="Product"/> as part of an ordered collection.
/// </summary>
public sealed record ImageReference : ValueObject
{
    public const int MaxAltTextLength = 200;

    public string Url { get; private init; } = string.Empty;
    public string AltText { get; private init; } = string.Empty;
    public int DisplayOrder { get; private init; }

    private ImageReference()
    {
    }

    public static Result<ImageReference> Create(string? url, string? altText, int displayOrder)
    {
        var trimmedUrl = url?.Trim() ?? string.Empty;
        if (trimmedUrl.Length == 0 ||
            !Uri.TryCreate(trimmedUrl, UriKind.Absolute, out _))
        {
            return Result.Fail(ImageReferenceErrors.InvalidUrl());
        }

        var trimmedAlt = altText?.Trim() ?? string.Empty;
        if (trimmedAlt.Length == 0)
        {
            return Result.Fail(ImageReferenceErrors.AltTextEmpty());
        }

        if (trimmedAlt.Length > MaxAltTextLength)
        {
            return Result.Fail(ImageReferenceErrors.AltTextTooLong(MaxAltTextLength));
        }

        if (displayOrder < 0)
        {
            return Result.Fail(ImageReferenceErrors.NegativeDisplayOrder());
        }

        return new ImageReference
        {
            Url = trimmedUrl,
            AltText = trimmedAlt,
            DisplayOrder = displayOrder
        };
    }
}
