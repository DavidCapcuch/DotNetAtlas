using Platform.SharedKernel.Exceptions;

namespace Invoicing.Application.Common.Exceptions;

/// <summary>
/// Bug-class exception — surfaces through the DLT pipeline when the order total
/// and the captured payment amount disagree for the same <see cref="OrderId"/>
/// (example-mapping 1.4). Thrown by <c>IssueInvoiceCommandHandler</c> after the
/// converged-projection precondition check.
/// </summary>
/// <remarks>
/// <para>
/// This is a cross-aggregate integrity violation (Order vs Payment), not an
/// <c>Invoice</c>/<c>CreditNote</c> invariant — it lives in the Application layer
/// because the comparison is only meaningful once both projection halves have
/// converged onto a single <c>pending_invoices</c> row.
/// </para>
/// <para>
/// Inherits <see cref="DataIntegrityException"/> so the consumer middleware's
/// existing <c>catch (CriticalException)</c> branch DLTs it without change.
/// Carries the source values as typed properties so observability does not have
/// to parse <see cref="Exception.Message"/>.
/// </para>
/// </remarks>
public sealed class InvoiceTotalMismatchException(
    decimal orderTotal,
    decimal paymentAmount,
    Guid orderId)
    : DataIntegrityException(
        "Invoicing.TotalMismatch",
        $"Total mismatch on order {orderId}: order total {orderTotal}, payment amount {paymentAmount}.")
{
    public decimal OrderTotal { get; } = orderTotal;

    public decimal PaymentAmount { get; } = paymentAmount;

    public Guid OrderId { get; } = orderId;
}
