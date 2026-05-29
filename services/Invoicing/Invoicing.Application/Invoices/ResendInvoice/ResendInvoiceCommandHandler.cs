using FluentResults;
using Invoicing.Application.Common.Data;
using Invoicing.Domain.Common.Errors;
using Invoicing.Domain.Invoices.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.CQRS;

namespace Invoicing.Application.Invoices.ResendInvoice;

/// <summary>
/// Handles <see cref="ResendInvoiceCommand"/> — admin-only invoice re-delivery.
/// </summary>
/// <remarks>
/// <para>
/// V1 is deliberately minimal: verify the invoice exists (404 otherwise) and is in a
/// resendable state (409 otherwise). HTTP idempotency is provided by FastEndpoints'
/// <c>.Idempotency()</c> filter (ADR-0013) — the cache slot returns the cached 204 on
/// double-clicks before this handler ever runs again.
/// </para>
/// <para>
/// Deferred to a later milestone: the <c>invoice_delivery_log</c> insert + outbox row
/// keyed <c>(InvoiceId, Channel, Attempt)</c> described in <c>invoicing.md § 12</c>.
/// The delivery-log table requires a user-generated EF migration (CLAUDE.md). When that
/// table exists the handler will SELECT MAX(Attempt) and INSERT Attempt+1 inside a
/// transaction; for now the resend is a no-op observability event with the 204
/// representing acknowledgement rather than work performed.
/// </para>
/// <para>
/// Wave 1 closeout follow-up H2: the v1-stub disclosure now also flows into the
/// OpenAPI <c>Description</c> on <c>ResendInvoiceEndpoint</c> so admin tooling
/// reading the spec cannot misinterpret 204 as completed delivery. See the
/// followups summary in
/// <c>docs/implementation-prompts/session-summaries/invoicing-followups.md</c>.
/// </para>
/// </remarks>
internal sealed class ResendInvoiceCommandHandler : ICommandHandler<ResendInvoiceCommand>
{
    private readonly IInvoicingDbContext _dbContext;
    private readonly ILogger<ResendInvoiceCommandHandler> _logger;

    public ResendInvoiceCommandHandler(
        IInvoicingDbContext dbContext,
        ILogger<ResendInvoiceCommandHandler> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(ResendInvoiceCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Pure PK lookup (ADR-0022): no business name and no include-shape — the owned
        // invoice_lines / invoice_vat_lines collections auto-load — so this is inline LINQ
        // rather than a spec. AsNoTracking because the resend is read-only (status check).
        var invoice = await _dbContext.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == command.InvoiceId, ct);

        if (invoice is null)
        {
            return Result.Fail(InvoicingErrors.InvoiceNotFound(command.InvoiceId));
        }

        if (invoice.Status != InvoiceStatus.Issued && invoice.Status != InvoiceStatus.Delivered)
        {
            // Resend only makes sense from Issued/Delivered. Draft has no PDF; Cancelled /
            // Archived are terminal. Surface as a recoverable state-conflict so the caller
            // gets a 409 + actionable error code (mapped by InvoicingErrorCodes →
            // ResultsExtensions in the API layer).
            return Result.Fail(InvoicingErrors.InvalidInvoiceTransition(
                from: invoice.Status.Name,
                to: "ResendRequested"));
        }

        _logger.LogInformation(
            "Admin resend acknowledged for invoice {InvoiceId} ({InvoiceNumber}); status={Status}.",
            invoice.Id,
            invoice.InvoiceNumber?.Value,
            invoice.Status.Name);

        return Result.Ok();
    }
}
