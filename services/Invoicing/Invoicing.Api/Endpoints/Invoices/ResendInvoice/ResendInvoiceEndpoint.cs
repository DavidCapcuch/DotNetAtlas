using System.Net;
using FastEndpoints;
using Invoicing.Api.Common.Authorization;
using Invoicing.Api.Common.Extensions;
using Invoicing.Application.Invoices.ResendInvoice;
using Platform.Api.Extensions;
using Serilog.Context;

namespace Invoicing.Api.Endpoints.Invoices.ResendInvoice;

/// <summary>
/// <c>POST /api/v1/invoicing/invoices/{invoiceId}/resend</c> — admin-only re-delivery
/// trigger. Returns 204 on success; 404 if the invoice is unknown; 409 if it is in a
/// non-resendable state (Draft / Cancelled / Archived).
/// </summary>
/// <remarks>
/// <para>
/// FastEndpoints' built-in <c>.Idempotency()</c> filter (per ADR-0013) is attached so
/// a double-clicked admin resend returns the same 204 from the Redis-backed output cache
/// instead of running the handler twice. ADR-0013's worked example sets
/// <c>AdditionalCacheKey</c>, but FastEndpoints 7.0.1's <see cref="IdempotencyOptions"/>
/// does not surface that property — instead it ships <c>Authorization</c> in the default
/// <see cref="IdempotencyOptions.AdditionalHeaders"/>, which the <c>OutputCachePolicy</c>
/// wires into <c>CacheVaryByRules.HeaderNames</c>. Net effect: the cache slot varies by
/// bearer token, so two admins reusing the same UUID never share responses.
/// </para>
/// <para>
/// <b>v1 stub behaviour:</b> the 204 represents
/// acknowledgement, not delivery. <see cref="ResendInvoiceCommandHandler"/> validates
/// state but does NOT yet insert the <c>invoice_delivery_log</c> row or emit an
/// outbox event — both deferred per <c>invoicing.md § 12</c> until a downstream
/// delivery consumer lands. The Idempotency-Key cache slot pins this no-op for 24 h,
/// so admin tooling MUST NOT interpret 204 as work performed. The OpenAPI
/// <c>Description</c> below propagates the same disclosure to spec consumers.
/// </para>
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
            s.Summary = "Resend invoice to buyer (admin only, v1 stub). Idempotent on Idempotency-Key.";
            s.Description =
                "v1 stub: validates invoice state and caches the response under Idempotency-Key " +
                "for 24 h per ADR-0013, but does NOT yet insert invoice_delivery_log rows or emit " +
                "outbox events (deferred per invoicing.md § 12 until a downstream delivery consumer " +
                "is ready). The 204 represents acknowledgement, not delivery — admin tooling must " +
                "NOT interpret it as work performed. See ResendInvoiceCommandHandler xmldoc.";
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
