using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;

namespace Invoicing.Domain.Common.ValueObjects;

/// <summary>
/// Stock-Keeping Unit identifier snapshotted onto invoice lines.
/// Opaque non-empty string (up to 64 chars), assigned by the Catalog BC. Invoicing only
/// carries the value for display / audit — it does not validate semantics beyond shape.
/// </summary>
/// <param name="Value">Non-empty, trimmed, max 64 chars.</param>
public sealed record Sku(string Value) : ValueObject
{
    public const int MaxLength = 64;

    public static Result<Sku> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            return Result.Fail<Sku>(new ValidationError(
                nameof(Value), $"Sku is required and must be \u2264 {MaxLength} chars.", "Invoicing.InvalidSku"));
        }

        return Result.Ok(new Sku(value.Trim()));
    }

    public override string ToString() => Value;
}
