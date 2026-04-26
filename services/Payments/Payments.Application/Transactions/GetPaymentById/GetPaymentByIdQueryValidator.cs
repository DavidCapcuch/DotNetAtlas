using FluentValidation;

namespace Payments.Application.Transactions.GetPaymentById;

internal sealed class GetPaymentByIdQueryValidator : AbstractValidator<GetPaymentByIdQuery>
{
    public GetPaymentByIdQueryValidator()
    {
        RuleFor(q => q.PaymentId).NotEmpty();
    }
}
