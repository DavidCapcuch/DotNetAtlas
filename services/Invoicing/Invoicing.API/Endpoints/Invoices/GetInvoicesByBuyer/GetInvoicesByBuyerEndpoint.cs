using System.Net;
using FastEndpoints;
using Invoicing.API.Common.Extensions;
using Invoicing.Application.Invoices.GetInvoicesByBuyer;
using Serilog.Context;

namespace Invoicing.API.Endpoints.Invoices.GetInvoicesByBuyer;

/// <summary>
/// <c>GET /api/v1/invoicing/invoices?skip=&amp;take=</c> — paged list of the calling
/// buyer's invoices. Admin override is deferred to v2+; v1 always scopes to the caller's
/// JWT subject.
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
        Summary(s =>
        {
            s.Summary = "List the calling buyer's invoices, most recent first.";
        });
        Description(b =>
        {
            b.Produces<GetInvoicesByBuyerResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.UnprocessableEntity);
        });
    }

    public override async Task HandleAsync(GetInvoicesByBuyerRequest req, CancellationToken ct)
    {
        var buyerId = User.GetBuyerIdOrNull();
        if (buyerId is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        using var _ = LogContext.PushProperty("BuyerId", buyerId.Value);

        var query = new GetInvoicesByBuyerQuery
        {
            BuyerId = buyerId.Value,
            Skip = req.Skip,
            Take = req.Take,
        };

        var result = await _handler.HandleAsync(query, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failure => Send.SendErrorResponseAsync(failure, ct));
    }
}
