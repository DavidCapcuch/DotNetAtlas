using FluentValidation;

namespace Invoicing.Application.Invoices.GetInvoicesByBuyer;

public sealed class GetInvoicesByBuyerQueryValidator : AbstractValidator<GetInvoicesByBuyerQuery>
{
    public GetInvoicesByBuyerQueryValidator()
    {
        RuleFor(q => q.BuyerId).NotEmpty();
        RuleFor(q => q.Skip).GreaterThanOrEqualTo(0);
        RuleFor(q => q.Take).InclusiveBetween(1, 100);
    }
}
