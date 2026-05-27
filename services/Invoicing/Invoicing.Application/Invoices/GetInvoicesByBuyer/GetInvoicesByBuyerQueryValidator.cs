using FluentValidation;

namespace Invoicing.Application.Invoices.GetInvoicesByBuyer;

public sealed class GetInvoicesByBuyerQueryValidator : AbstractValidator<GetInvoicesByBuyerQuery>
{
    public GetInvoicesByBuyerQueryValidator()
    {
        RuleFor(q => q.BuyerId).NotEmpty();
        RuleFor(q => q.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}
