namespace EShop.BFF.Api.Endpoints.BasketMutations;

/// <summary>Body binding for <c>POST /api/v1/bff/basket/items</c> (bff.md § 3.6).</summary>
public sealed class AddBasketItemRequest
{
    public Guid ProductId { get; set; }

    public int Quantity { get; set; }
}
