using System.Net;
using FastEndpoints;
using Payments.Api.Common.Authorization;
using Payments.Api.Common.Extensions;
using Payments.Application.Transactions.GetPaymentsByOrder;
using Platform.CQRS;
using Serilog.Context;

namespace Payments.Api.Endpoints.Payments.GetPaymentsByOrder;

/// <summary>
/// <c>GET /api/v1/payments?orderId=...</c> — admin lookup of all payment
/// transactions for a given order. Returns an empty list (not 404) if no
/// payments exist for that order. Gated on
/// <see cref="AuthPolicies.PaymentsAdmin"/>.
/// </summary>
internal sealed class GetPaymentsByOrderEndpoint
    : Endpoint<GetPaymentsByOrderRequest, GetPaymentsByOrderResponse>
{
    private readonly IQueryHandler<GetPaymentsByOrderQuery, GetPaymentsByOrderResponse> _handler;

    public GetPaymentsByOrderEndpoint(
        IQueryHandler<GetPaymentsByOrderQuery, GetPaymentsByOrderResponse> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Get(string.Empty);
        Version(1);
        Group<PaymentsGroup>();
        Policies(AuthPolicies.PaymentsAdmin);
        Summary(s =>
        {
            s.Summary = "List payment transactions for an order (admin only).";
            s.ExampleRequest = new GetPaymentsByOrderRequest
            {
                OrderId = new Guid("0198B2A9-CB8C-744B-8CDD-0B64727CF2FC"),
            };
        });
        Description(b =>
        {
            b.Produces<GetPaymentsByOrderResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.BadRequest);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.Forbidden);
        });
    }

    public override async Task HandleAsync(GetPaymentsByOrderRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);

        using var _ = LogContext.PushProperty("OrderId", req.OrderId);

        var query = new GetPaymentsByOrderQuery(req.OrderId);
        var result = await _handler.HandleAsync(query, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failure => Send.SendErrorResponseAsync(failure, ct));
    }
}
