using System.Net;
using FastEndpoints;
using Invoicing.Api.Common.Extensions;
using Invoicing.Application.Invoices.GetInvoicesByBuyer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Platform.Api.Extensions;
using Serilog.Context;

namespace Invoicing.Api.Endpoints.Invoices.GetInvoicesByBuyer;

/// <summary>
/// <c>GET /api/v1/invoicing/invoices?pageNumber=&amp;pageSize=&amp;buyerId=</c> — paged
/// list of invoices. Buyer callers always scope to their own JWT subject; admins may
/// pass <c>?buyerId={guid}</c> to list another buyer's invoices. A non-admin caller
/// passing a <c>buyerId</c> different from their own is rejected with 403.
/// </summary>
internal sealed class GetInvoicesByBuyerEndpoint
    : Endpoint<GetInvoicesByBuyerRequest, GetInvoicesByBuyerResponse>
{
    private readonly Platform.CQRS.IQueryHandler<GetInvoicesByBuyerQuery, GetInvoicesByBuyerResponse> _handler;

    public GetInvoicesByBuyerEndpoint(
        Platform.CQRS.IQueryHandler<GetInvoicesByBuyerQuery, GetInvoicesByBuyerResponse> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Get(string.Empty);
        Version(1);
        Group<InvoicesGroup>();
        // Declarative auth — pin the JWT bearer scheme so a future global-middleware
        // refactor cannot silently un-gate this endpoint. Identity checks (buyer
        // ownership / admin override) are enforced inside the handler below.
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "List invoices for a buyer (caller-scoped for buyers; admin override via ?buyerId=).";
        });
        Description(b =>
        {
            b.Produces<GetInvoicesByBuyerResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.Forbidden);
            b.Produces((int)HttpStatusCode.UnprocessableEntity);
        });
    }

    public override async Task HandleAsync(GetInvoicesByBuyerRequest req, CancellationToken ct)
    {
        var callerBuyerId = User.GetBuyerIdOrNull();
        var isAdmin = User.IsInvoicingAdmin();

        if (!isAdmin && callerBuyerId is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        // Admin override: req.BuyerId scopes the response to another buyer for admins,
        // but a non-admin caller asking for a buyer other than themselves is an explicit
        // boundary violation — 403 rather than silently scoping to self so admin tooling
        // accidentally invoked without admin privs surfaces loudly.
        Guid effectiveBuyerId;
        if (req.BuyerId is { } requestedBuyerId)
        {
            if (!isAdmin && requestedBuyerId != callerBuyerId)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            effectiveBuyerId = requestedBuyerId;
        }
        else
        {
            effectiveBuyerId = callerBuyerId ?? Guid.Empty;
        }

        using var _ = LogContext.PushProperty("BuyerId", effectiveBuyerId);
        using var __ = LogContext.PushProperty("IsAdmin", isAdmin);

        var query = new GetInvoicesByBuyerQuery
        {
            BuyerId = effectiveBuyerId,
            PageNumber = req.PageNumber,
            PageSize = req.PageSize,
        };

        var result = await _handler.HandleAsync(query, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failure => Send.SendErrorResponseAsync(failure, ct));
    }
}
