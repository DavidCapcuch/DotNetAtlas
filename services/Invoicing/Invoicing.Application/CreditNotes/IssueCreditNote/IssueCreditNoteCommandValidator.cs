using FluentValidation;

namespace Invoicing.Application.CreditNotes.IssueCreditNote;

/// <summary>
/// User-shape validation for <see cref="IssueCreditNoteCommand"/>. The command has no
/// user-facing surface (it is issued by the convergence path inside the BC), so this
/// validator only guards against an obviously empty correlation id; deeper invariants
/// surface as <c>DataIntegrityException</c> from the handler.
/// </summary>
internal sealed class IssueCreditNoteCommandValidator : AbstractValidator<IssueCreditNoteCommand>
{
    public IssueCreditNoteCommandValidator()
    {
        RuleFor(c => c.CorrelationId).NotEmpty();
    }
}
