namespace Payments.Api.Endpoints.Payments.GetPaymentsByOrder;

/// <summary>
/// HTTP request shape for <c>GET /api/v1/payments?orderId=...</c>. FastEndpoints
/// binds plain properties from the query string by default for GET verbs, so
/// no <c>[QueryParam]</c> attribute is needed; the absence keeps the typed
/// test helper (<c>GETAsync&lt;TEndpoint, TRequest, TResponse&gt;</c>) happy
/// when building the URL from the request instance.
/// </summary>
public sealed class GetPaymentsByOrderRequest
{
    public required Guid OrderId { get; init; }
}
