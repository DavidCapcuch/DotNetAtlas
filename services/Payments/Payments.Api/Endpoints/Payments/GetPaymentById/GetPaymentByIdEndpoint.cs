using System.Net;
using FastEndpoints;
using Payments.Api.Common.Extensions;
using Payments.Application.Transactions.GetPaymentById;
using Payments.Infrastructure.Common.Authorization;
using Platform.CQRS;
using Serilog.Context;

namespace Payments.Api.Endpoints.Payments.GetPaymentById;

/// <summary>
/// <c>GET /api/v1/payments/{paymentId}</c> — single-payment admin lookup.
/// Gated on <see cref="AuthPolicies.PaymentsAdmin"/> (admin role + <c>payments.read</c>
/// scope per ADR-0010).
/// </summary>
internal sealed class GetPaymentByIdEndpoint
    : Endpoint<GetPaymentByIdRequest, GetPaymentByIdResponse>
{
    private readonly IQueryHandler<GetPaymentByIdQuery, GetPaymentByIdResponse> _handler;

    public GetPaymentByIdEndpoint(
        IQueryHandler<GetPaymentByIdQuery, GetPaymentByIdResponse> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Get("{PaymentId:guid}");
        Version(1);
        Group<PaymentsGroup>();
        Policies(AuthPolicies.PaymentsAdmin);
        Summary(s =>
        {
            s.Summary = "Get a payment transaction by id (admin only).";
            s.ExampleRequest = new GetPaymentByIdRequest
            {
                PaymentId = new Guid("0198B2A9-CB8C-744B-8CDD-0B64727CF2FC"),
            };
        });
        Description(b =>
        {
            b.Produces<GetPaymentByIdResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.NotFound);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.Forbidden);
        });
    }

    public override async Task HandleAsync(GetPaymentByIdRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);

        using var _ = LogContext.PushProperty("PaymentId", req.PaymentId);

        var query = new GetPaymentByIdQuery(req.PaymentId);
        var result = await _handler.HandleAsync(query, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failure => Send.SendErrorResponseAsync(failure, ct));
    }
}
