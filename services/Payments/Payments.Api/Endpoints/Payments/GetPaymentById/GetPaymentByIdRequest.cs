using FastEndpoints;

namespace Payments.Api.Endpoints.Payments.GetPaymentById;

/// <summary>
/// HTTP request shape for <c>GET /api/v1/payments/{paymentId}</c>.
/// </summary>
public sealed class GetPaymentByIdRequest
{
    [RouteParam]
    public required Guid PaymentId { get; init; }
}
