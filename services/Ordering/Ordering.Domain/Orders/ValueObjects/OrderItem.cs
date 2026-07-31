using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.Domain.Orders.ValueObjects;

/// <summary>
/// A single line on an <see cref="Order"/>: one product (by id + snapshot),
/// a quantity, a unit price, and the computed line total. Value-object, not
/// an entity — <see cref="Order"/>'s items list has no independent lifecycle
/// (per <c>ordering.md § 3.1 Vernon rule 2</c>).
/// </summary>
public sealed record OrderItem : ValueObject
{
    public Guid ProductId { get; private init; }
    public ProductSnapshot ProductSnapshot { get; private init; } = null!;
    public int Quantity { get; private init; }
    public Money UnitPrice { get; private init; } = null!;
    public Money LineTotal { get; private init; } = null!;

    private OrderItem()
    {
    }

    /// <summary>
    /// Creates an <see cref="OrderItem"/>. Quantity and unit price must both be strictly
    /// positive — Ordering-local invariant I-8. Money itself is permissive on sign, so
    /// positivity is enforced here at the VO boundary. The line total is stored rather
    /// than recomputed — lets EF Core map it as an owned value without <c>[NotMapped]</c>
    /// hacks (<c>ordering.md § 4.3</c>).
    /// </summary>
    public static Result<OrderItem> Create(
        Guid productId,
        ProductSnapshot productSnapshot,
        int quantity,
        Money unitPrice)
    {
        ArgumentNullException.ThrowIfNull(productSnapshot);
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (productId == Guid.Empty)
        {
            return Result.Fail(new ValidationError(
                nameof(ProductId), "Product id must not be empty.", "OrderItem.ProductIdEmpty"));
        }

        if (quantity <= 0)
        {
            return Result.Fail(new ValidationError(
                nameof(Quantity), "Quantity must be strictly positive.", "OrderItem.QuantityNotPositive"));
        }

        if (unitPrice.Amount <= 0)
        {
            return Result.Fail(new ValidationError(
                nameof(UnitPrice), "Unit price must be strictly positive.", "OrderItem.UnitPriceNotPositive"));
        }

        // Inputs are pre-validated above (quantity > 0, unitPrice.Amount > 0)
        // so Money.Create cannot legitimately fail here. Routing through the
        // factory keeps the construction defensive against future Money
        // invariants without bypassing them.
        var lineTotalResult = Money.Create(unitPrice.Amount * quantity, unitPrice.Currency);
        if (lineTotalResult.IsFailed)
        {
            throw new DataIntegrityException(
                "OrderItem.InvalidLineTotal",
                $"Computed LineTotal failed Money.Create: {string.Join("; ", lineTotalResult.Errors.Select(e => e.Message))}.");
        }

        return new OrderItem
        {
            ProductId = productId,
            ProductSnapshot = productSnapshot,
            Quantity = quantity,
            UnitPrice = unitPrice,
            LineTotal = lineTotalResult.Value,
        };
    }
}
