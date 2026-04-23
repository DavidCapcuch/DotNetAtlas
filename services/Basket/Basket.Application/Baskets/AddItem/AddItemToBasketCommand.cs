using Platform.CQRS;

namespace Basket.Application.Baskets.AddItem;

/// <summary>
/// Adds <paramref name="Quantity"/> units of <paramref name="ProductId"/> to the
/// basket owned by <paramref name="UserId"/>. If the basket does not yet exist it is
/// lazily created. If the product is already present the line quantities collapse
/// (see <c>Basket.AddItem</c>). Snapshots are captured from Catalog via the ACL port.
/// </summary>
/// <param name="UserId">The basket owner (JWT <c>sub</c> at the API seam).</param>
/// <param name="ProductId">Catalog product identifier.</param>
/// <param name="Quantity">Number of units to add; 1..1000.</param>
public sealed record AddItemToBasketCommand(Guid UserId, Guid ProductId, int Quantity) : ICommand;
