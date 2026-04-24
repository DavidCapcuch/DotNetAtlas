using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;

namespace Inventory.Domain.StockItems.ValueObjects;

/// <summary>
/// Strong-typed wrapper around the reservation <see cref="Guid"/> identifier supplied by
/// the checkout saga. Distinguishes from <c>OrderId</c> at call sites.
/// </summary>
public sealed record ReservationId : ValueObject
{
    public Guid Value { get; private init; }

    private ReservationId()
    {
    }

    /// <summary>
    /// Creates a <see cref="ReservationId"/>. Rejects <see cref="Guid.Empty"/>.
    /// </summary>
    public static Result<ReservationId> Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            return Result.Fail(new ValidationError(
                propertyName: nameof(Value),
                errorMessage: "ReservationId must not be the empty Guid.",
                errorCode: "ReservationId.Empty"));
        }

        return new ReservationId { Value = value };
    }

    public override string ToString() => Value.ToString();
}
