using System.Net;
using FastEndpoints;
using Invoicing.API.Common.Extensions;
using Invoicing.Application.Invoices.GetInvoiceById;
using Invoicing.Application.Invoices.GetInvoiceByOrderId;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Serilog.Context;

namespace Invoicing.API.Endpoints.Invoices.GetInvoiceByOrderId;

/// <summary>
/// <c>GET /api/v1/invoicing/invoices/by-order/{orderId}</c> — buyer-scoped lookup of the
/// invoice that settles a given order. Cross-buyer queries return 404 (existence not
/// leaked); admins may read any invoice.
/// </summary>
internal sealed class GetInvoiceByOrderIdEndpoint
    : Endpoint<GetInvoiceByOrderIdRequest, GetInvoiceByIdResponse>
{
    private readonly Platform.CQRS.IQueryHandler<GetInvoiceByOrderIdQuery, GetInvoiceByIdResponse> _handler;

    public GetInvoiceByOrderIdEndpoint(
        Platform.CQRS.IQueryHandler<GetInvoiceByOrderIdQuery, GetInvoiceByIdResponse> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Get("by-order/{OrderId}");
        Version(1);
        Group<InvoicesGroup>();
        // Declarative auth — pin the JWT bearer scheme so a future global-middleware
        // refactor cannot silently un-gate this endpoint. Identity checks (buyer
        // ownership / admin override) are enforced inside the handler below.
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Find the invoice settling a given order (own for buyer; any for admin).";
            s.ExampleRequest = new GetInvoiceByOrderIdRequest
            {
                OrderId = new Guid("0198B2A9-CB8C-744B-8CDD-0B64727CF2FC"),
            };
        });
        Description(b =>
        {
            b.Produces<GetInvoiceByIdResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.NotFound);
            b.Produces((int)HttpStatusCode.Unauthorized);
        });
    }

    public override async Task HandleAsync(GetInvoiceByOrderIdRequest req, CancellationToken ct)
    {
        var buyerId = User.GetBuyerIdOrNull();
        var isAdmin = User.IsInvoicingAdmin();

        if (!isAdmin && buyerId is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        using var _ = LogContext.PushProperty("OrderId", req.OrderId);
        using var __ = LogContext.PushProperty("IsAdmin", isAdmin);

        var query = new GetInvoiceByOrderIdQuery
        {
            OrderId = req.OrderId,
            BuyerId = buyerId ?? Guid.Empty,
            IsAdmin = isAdmin,
        };

        var result = await _handler.HandleAsync(query, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failure => Send.SendErrorResponseAsync(failure, ct));
    }
}
