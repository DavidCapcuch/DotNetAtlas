using FluentValidation;

namespace Invoicing.Application.CreditNotes.GetCreditNoteById;

public sealed class GetCreditNoteByIdQueryValidator : AbstractValidator<GetCreditNoteByIdQuery>
{
    public GetCreditNoteByIdQueryValidator()
    {
        RuleFor(q => q.CreditNoteId).NotEmpty();

        RuleFor(q => q.BuyerId)
            .NotEmpty()
            .When(q => !q.IsAdmin);
    }
}
