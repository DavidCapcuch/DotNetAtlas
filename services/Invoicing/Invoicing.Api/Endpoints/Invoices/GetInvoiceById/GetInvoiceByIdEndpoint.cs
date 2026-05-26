using System.Net;
using FastEndpoints;
using Invoicing.Api.Common.Extensions;
using Invoicing.Application.Invoices.GetInvoiceById;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Serilog.Context;

namespace Invoicing.Api.Endpoints.Invoices.GetInvoiceById;

/// <summary>
/// <c>GET /api/v1/invoicing/invoices/{invoiceId}</c> — single-invoice read endpoint.
/// Authorisation enforced inside the query handler: a buyer requesting an invoice owned
/// by a different buyer surfaces as 404 (not 403) to avoid leaking existence; admins
/// read any invoice.
/// </summary>
internal sealed class GetInvoiceByIdEndpoint
    : Endpoint<GetInvoiceByIdRequest, GetInvoiceByIdResponse>
{
    private readonly Platform.CQRS.IQueryHandler<GetInvoiceByIdQuery, GetInvoiceByIdResponse> _handler;

    public GetInvoiceByIdEndpoint(
        Platform.CQRS.IQueryHandler<GetInvoiceByIdQuery, GetInvoiceByIdResponse> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Get("{InvoiceId}");
        Version(1);
        Group<InvoicesGroup>();
        // Declarative auth — pin the JWT bearer scheme so a future global-middleware
        // refactor cannot silently un-gate this endpoint. Identity checks (buyer
        // ownership / admin override) are enforced inside the handler below.
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Get an invoice by id (own invoice for buyer; any for admin).";
            s.ExampleRequest = new GetInvoiceByIdRequest
            {
                InvoiceId = new Guid("0198B2A9-CB8C-744B-8CDD-0B64727CF2FC"),
            };
        });
        Description(b =>
        {
            b.Produces<GetInvoiceByIdResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.NotFound);
            b.Produces((int)HttpStatusCode.Unauthorized);
        });
    }

    public override async Task HandleAsync(GetInvoiceByIdRequest req, CancellationToken ct)
    {
        var buyerId = User.GetBuyerIdOrNull();
        var isAdmin = User.IsInvoicingAdmin();

        if (!isAdmin && buyerId is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        using var _ = LogContext.PushProperty("InvoiceId", req.InvoiceId);
        using var __ = LogContext.PushProperty("IsAdmin", isAdmin);

        var query = new GetInvoiceByIdQuery
        {
            InvoiceId = req.InvoiceId,
            BuyerId = buyerId ?? Guid.Empty,
            IsAdmin = isAdmin,
        };

        var result = await _handler.HandleAsync(query, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failure => Send.SendErrorResponseAsync(failure, ct));
    }
}
