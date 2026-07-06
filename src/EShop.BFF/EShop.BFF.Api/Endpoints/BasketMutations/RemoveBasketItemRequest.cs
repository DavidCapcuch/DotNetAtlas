namespace EShop.BFF.Api.Endpoints.BasketMutations;

/// <summary>Route binding for <c>DELETE /api/v1/bff/basket/items/{productId}</c> (bff.md § 3.6).</summary>
public sealed class RemoveBasketItemRequest
{
    public Guid ProductId { get; set; }
}
