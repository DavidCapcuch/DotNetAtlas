using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

namespace Basket.Domain.Baskets.Events;

/// <summary>
/// In-process event — raised by <c>Basket.AddItem</c> for both the initial add of
/// a product and for subsequent adds of the same product (which collapse into a
/// quantity bump on the existing line).
/// </summary>
/// <remarks>
/// <see cref="Quantity"/> is the quantity that was added in this call, not the
/// resulting line total (basket.md § 7).
/// </remarks>
public sealed record ItemAddedToBasketDomainEvent : DomainEvent
{
    public required Guid UserId { get; init; }

    public required Guid ProductId { get; init; }

    /// <summary>
    /// Quantity added in this call. For a bump on an existing line this is the
    /// delta, not the new total.
    /// </summary>
    public required int Quantity { get; init; }

    /// <summary>
    /// Snapshot unit price captured at the time this product was first added.
    /// </summary>
    public required Money CapturedPrice { get; init; }
}
