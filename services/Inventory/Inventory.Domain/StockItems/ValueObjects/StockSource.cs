using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;

namespace Inventory.Domain.StockItems.ValueObjects;

/// <summary>
/// Where an inbound stock movement came from. A light enum-like string token —
/// intentionally free-form in v1. Canonical values exposed as static readonly fields.
/// </summary>
public sealed record StockSource : ValueObject
{
    public const int MaxLength = 64;

    public static readonly StockSource ReceivingDock = new() { Value = "receiving-dock" };
    public static readonly StockSource Returns = new() { Value = "returns" };
    public static readonly StockSource TransferIn = new() { Value = "transfer-in" };

    public string Value { get; private init; } = string.Empty;

    private StockSource()
    {
    }

    /// <summary>
    /// Creates a validated <see cref="StockSource"/>. Input is trimmed.
    /// </summary>
    public static Result<StockSource> Create(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return Result.Fail(new ValidationError(
                propertyName: nameof(Value),
                errorMessage: "StockSource must not be empty.",
                errorCode: "StockSource.Empty"));
        }

        if (trimmed.Length > MaxLength)
        {
            return Result.Fail(new ValidationError(
                propertyName: nameof(Value),
                errorMessage: $"StockSource must not exceed {MaxLength} characters.",
                errorCode: "StockSource.TooLong"));
        }

        return new StockSource { Value = trimmed };
    }

    public override string ToString() => Value;
}
