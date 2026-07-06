using System.Net;

namespace EShop.BFF.Infrastructure.Clients.Basket;

/// <summary>
/// Basket's relayed verdict on a forwarded mutation (bff.md § 3.6): the status plus — when Basket wrote one
/// (RFC 9457 problem details on a decline) — its body and content type, forwarded verbatim so the caller
/// keeps the error context (e.g. a 409's EmptyBasket vs MaxItemsReached). A 204 carries no body.
/// </summary>
internal sealed record BasketWriteVerdict(HttpStatusCode Status, string? Body = null, string? ContentType = null);

/// <summary>
/// BFF-internal request body for <c>POST /api/v1/basket/items</c> (bff.md § 4.2). The snapshot price is
/// captured server-side by Basket at add-time, so the BFF forwards only the product + quantity.
/// </summary>
internal sealed record AddItemDto(Guid ProductId, int Quantity);

/// <summary>
/// BFF-internal request body for <c>PUT /api/v1/basket/items/{productId}/quantity</c> (bff.md § 4.2). The
/// product is in the route; only the new quantity rides the body.
/// </summary>
internal sealed record ChangeItemQuantityDto(int NewQuantity);
