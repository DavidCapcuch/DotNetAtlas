namespace EShop.BFF.Api.Endpoints.BasketMutations;

/// <summary>
/// Binding for <c>PUT /api/v1/bff/basket/items/{productId}/quantity</c> (bff.md § 3.6) — <c>ProductId</c>
/// from the route, <c>NewQuantity</c> from the body.
/// </summary>
public sealed class ChangeBasketItemQuantityRequest
{
    public Guid ProductId { get; set; }

    public int NewQuantity { get; set; }
}
