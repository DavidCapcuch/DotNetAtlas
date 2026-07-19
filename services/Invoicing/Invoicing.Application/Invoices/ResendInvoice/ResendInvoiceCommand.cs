using Platform.CQRS;

namespace Invoicing.Application.Invoices.ResendInvoice;

/// <summary>
/// Admin-triggered re-delivery of a previously-issued invoice. v1 is intentionally minimal
/// — the handler verifies the invoice exists and is in <c>Issued</c> or <c>Delivered</c>
/// (otherwise 404 / 409). Idempotency for HTTP double-clicks is provided at the transport
/// layer by FastEndpoints' <c>.Idempotency()</c> filter (ADR-0013); the
/// <c>invoice_delivery_log</c> table + outbox publisher described in
/// <c>invoicing.md § 12</c> are not part of v1 (the table requires a
/// user-generated EF migration per CLAUDE.md).
/// </summary>
public sealed record ResendInvoiceCommand : ICommand
{
    public required Guid InvoiceId { get; init; }
}
