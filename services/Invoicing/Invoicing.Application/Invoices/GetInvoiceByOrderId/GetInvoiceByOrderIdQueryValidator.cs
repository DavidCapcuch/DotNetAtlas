using FluentValidation;

namespace Invoicing.Application.Invoices.GetInvoiceByOrderId;

public sealed class GetInvoiceByOrderIdQueryValidator : AbstractValidator<GetInvoiceByOrderIdQuery>
{
    public GetInvoiceByOrderIdQueryValidator()
    {
        RuleFor(q => q.OrderId).NotEmpty();

        RuleFor(q => q.BuyerId)
            .NotEmpty()
            .When(q => !q.IsAdmin);
    }
}
