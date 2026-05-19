using System.Net;
using FastEndpoints;
using Invoicing.API.Common.Extensions;
using Invoicing.Application.CreditNotes.GetCreditNoteById;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Serilog.Context;

namespace Invoicing.API.Endpoints.CreditNotes.GetCreditNoteById;

/// <summary>
/// <c>GET /api/v1/invoicing/credit-notes/{creditNoteId}</c> — single-credit-note read
/// endpoint. Authorisation enforced inside the query handler: a buyer requesting a
/// credit note owned by a different buyer surfaces as 404 (existence not leaked); admins
/// read any.
/// </summary>
internal sealed class GetCreditNoteByIdEndpoint
    : Endpoint<GetCreditNoteByIdRequest, GetCreditNoteByIdResponse>
{
    private readonly Platform.CQRS.IQueryHandler<GetCreditNoteByIdQuery, GetCreditNoteByIdResponse> _handler;

    public GetCreditNoteByIdEndpoint(
        Platform.CQRS.IQueryHandler<GetCreditNoteByIdQuery, GetCreditNoteByIdResponse> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Get("{CreditNoteId}");
        Version(1);
        Group<CreditNotesGroup>();
        // Declarative auth — pin the JWT bearer scheme so a future global-middleware
        // refactor cannot silently un-gate this endpoint. Identity checks (buyer
        // ownership / admin override) are enforced inside the handler below.
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Get a credit note by id (own credit note for buyer; any for admin).";
            s.ExampleRequest = new GetCreditNoteByIdRequest
            {
                CreditNoteId = new Guid("0198B2A9-CB8C-744B-8CDD-0B64727CF2FC"),
            };
        });
        Description(b =>
        {
            b.Produces<GetCreditNoteByIdResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.NotFound);
            b.Produces((int)HttpStatusCode.Unauthorized);
        });
    }

    public override async Task HandleAsync(GetCreditNoteByIdRequest req, CancellationToken ct)
    {
        var buyerId = User.GetBuyerIdOrNull();
        var isAdmin = User.IsInvoicingAdmin();

        if (!isAdmin && buyerId is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        using var _ = LogContext.PushProperty("CreditNoteId", req.CreditNoteId);
        using var __ = LogContext.PushProperty("IsAdmin", isAdmin);

        var query = new GetCreditNoteByIdQuery
        {
            CreditNoteId = req.CreditNoteId,
            BuyerId = buyerId ?? Guid.Empty,
            IsAdmin = isAdmin,
        };

        var result = await _handler.HandleAsync(query, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failure => Send.SendErrorResponseAsync(failure, ct));
    }
}
