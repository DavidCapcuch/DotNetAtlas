using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;

namespace Inventory.Domain.StockItems.ValueObjects;

/// <summary>
/// Non-negative count of stock units. Wraps the primitive to avoid passing naked ints
/// through the domain surface. Command paths that require strictly-positive input
/// (ReceiveStock, Reserve) enforce that additional rule at the command boundary.
/// </summary>
public sealed record Quantity : ValueObject
{
    public int Value { get; private init; }

    private Quantity()
    {
    }

    /// <summary>
    /// Creates a non-negative <see cref="Quantity"/>.
    /// </summary>
    public static Result<Quantity> Create(int value)
    {
        if (value < 0)
        {
            return Result.Fail(new ValidationError(
                propertyName: nameof(Value),
                errorMessage: "Quantity must be non-negative.",
                errorCode: "Quantity.Negative"));
        }

        return new Quantity { Value = value };
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
