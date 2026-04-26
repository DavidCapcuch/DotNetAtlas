using FastEndpoints;

namespace Basket.Api.Endpoints.Baskets.RemoveItem;

internal sealed class RemoveItemFromBasketRequest
{
    [BindFrom("productId")]
    public required Guid ProductId { get; init; }
}
