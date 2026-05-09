using FluentValidation;

namespace Invoicing.Application.Invoices.GetInvoiceById;

public sealed class GetInvoiceByIdQueryValidator : AbstractValidator<GetInvoiceByIdQuery>
{
    public GetInvoiceByIdQueryValidator()
    {
        RuleFor(q => q.InvoiceId).NotEmpty();

        // Non-admin callers must carry a real buyer id (the ownership check
        // in the handler relies on it). The admin branch deliberately sets
        // BuyerId = Guid.Empty per the GetInvoiceByIdQuery docstring.
        RuleFor(q => q.BuyerId)
            .NotEmpty()
            .When(q => !q.IsAdmin);
    }
}
