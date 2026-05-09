using System.Net;
using FastEndpoints;
using Invoicing.API.Common.Extensions;
using Invoicing.Application.Invoices.ResendInvoice;
using Invoicing.Infrastructure.Common.Authorization;
using Serilog.Context;

namespace Invoicing.API.Endpoints.Invoices.ResendInvoice;

/// <summary>
/// <c>POST /api/v1/invoicing/invoices/{invoiceId}/resend</c> — admin-only re-delivery
/// trigger. Returns 204 on success; 404 if the invoice is unknown; 409 if it is in a
/// non-resendable state (Draft / Cancelled / Archived).
/// </summary>
/// <remarks>
/// FastEndpoints' built-in <c>.Idempotency()</c> filter (per ADR-0013) is attached so
/// a double-clicked admin resend returns the same 202 from the Redis-backed output cache
/// instead of running the handler twice. ADR-0013's worked example sets
/// <c>AdditionalCacheKey</c>, but FastEndpoints 7.0.1's <see cref="IdempotencyOptions"/>
/// does not surface that property — instead it ships <c>Authorization</c> in the default
/// <see cref="IdempotencyOptions.AdditionalHeaders"/>, which the <c>OutputCachePolicy</c>
/// wires into <c>CacheVaryByRules.HeaderNames</c>. Net effect: the cache slot varies by
/// bearer token, so two admins reusing the same UUID never share responses.
/// </remarks>
internal sealed class ResendInvoiceEndpoint : Endpoint<ResendInvoiceRequest>
{
    private readonly Platform.CQRS.ICommandHandler<ResendInvoiceCommand> _handler;

    public ResendInvoiceEndpoint(Platform.CQRS.ICommandHandler<ResendInvoiceCommand> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Post("{InvoiceId}/resend");
        Version(1);
        Group<InvoicesGroup>();
        Policies(AuthPolicies.InvoicingAdmin);
        Idempotency(opts =>
        {
            // Header name + 24-hour TTL are the ADR-0013 § Implementation Notes contract.
            // The default Authorization-header inclusion in
            // IdempotencyOptions.AdditionalHeaders already partitions per buyer/admin, so
            // no AdditionalCacheKey is needed.
            opts.HeaderName = "Idempotency-Key";
            opts.CacheDuration = TimeSpan.FromHours(24);
        });
        Summary(s =>
        {
            s.Summary = "Resend invoice to buyer (admin only). Idempotent on Idempotency-Key.";
            s.ExampleRequest = new ResendInvoiceRequest
            {
                InvoiceId = new Guid("0198B2A9-CB8C-744B-8CDD-0B64727CF2FC"),
            };
        });
        Description(b =>
        {
            b.Produces((int)HttpStatusCode.NoContent);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.Forbidden);
            b.Produces((int)HttpStatusCode.NotFound);
            b.Produces((int)HttpStatusCode.Conflict);
        });
    }

    public override async Task HandleAsync(ResendInvoiceRequest req, CancellationToken ct)
    {
        using var _ = LogContext.PushProperty("InvoiceId", req.InvoiceId);

        var command = new ResendInvoiceCommand
        {
            InvoiceId = req.InvoiceId,
        };

        var result = await _handler.HandleAsync(command, ct);

        await result.MatchAsync(
            () => Send.NoContentAsync(ct),
            failure => Send.SendErrorResponseAsync(failure, ct));
    }
}
