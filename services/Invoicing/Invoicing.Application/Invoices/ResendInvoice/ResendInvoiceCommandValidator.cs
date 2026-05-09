using FluentValidation;

namespace Invoicing.Application.Invoices.ResendInvoice;

public sealed class ResendInvoiceCommandValidator : AbstractValidator<ResendInvoiceCommand>
{
    public ResendInvoiceCommandValidator()
    {
        RuleFor(c => c.InvoiceId).NotEmpty();
    }
}
