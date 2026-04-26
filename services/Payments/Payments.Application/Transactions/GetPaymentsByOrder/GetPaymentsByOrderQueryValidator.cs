using FluentValidation;

namespace Payments.Application.Transactions.GetPaymentsByOrder;

internal sealed class GetPaymentsByOrderQueryValidator : AbstractValidator<GetPaymentsByOrderQuery>
{
    public GetPaymentsByOrderQueryValidator()
    {
        RuleFor(q => q.OrderId).NotEmpty();
    }
}
