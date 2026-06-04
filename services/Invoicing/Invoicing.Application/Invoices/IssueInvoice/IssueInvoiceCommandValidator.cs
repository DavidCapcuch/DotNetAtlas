using FluentValidation;

namespace Invoicing.Application.Invoices.IssueInvoice;

/// <summary>
/// User-shape validation for <see cref="IssueInvoiceCommand"/>. The command has no
/// user-facing surface (it is issued by the convergence path inside the BC), so this
/// validator only guards against an obviously empty order id. Deeper invariants
/// (projection-row presence, total mismatch, etc.) are bug-class and surface as
/// <c>DataIntegrityException</c> from the handler — they cannot be reached without a
/// genuine system-level bug.
/// </summary>
internal sealed class IssueInvoiceCommandValidator : AbstractValidator<IssueInvoiceCommand>
{
    public IssueInvoiceCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
    }
}
