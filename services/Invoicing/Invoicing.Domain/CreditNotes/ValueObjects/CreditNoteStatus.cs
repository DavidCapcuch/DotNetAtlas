using Ardalis.SmartEnum;
using FluentResults;
using Invoicing.Domain.Common.Errors;

namespace Invoicing.Domain.CreditNotes.ValueObjects;

/// <summary>
/// FSM states for <c>CreditNote</c> per <c>invoicing.md § 2.2</c>.
/// Credit notes cannot themselves be cancelled \u2014 <c>Issued \u2192 Delivered \u2192 Archived</c>.
/// </summary>
public sealed class CreditNoteStatus : SmartEnum<CreditNoteStatus>
{
    public static readonly CreditNoteStatus Issued = new(nameof(Issued), 1);
    public static readonly CreditNoteStatus Delivered = new(nameof(Delivered), 2);
    public static readonly CreditNoteStatus Archived = new(nameof(Archived), 3);

    private CreditNoteStatus(string name, int value)
        : base(name, value)
    {
    }

    /// <summary>
    /// Returns <see cref="Result.Ok()"/> if the transition is allowed by the FSM, otherwise a
    /// <c>InvoicingErrors.InvalidCreditNoteTransition</c> validation error.
    /// </summary>
    public Result CanTransitionTo(CreditNoteStatus target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var allowed = (Value, target.Value) switch
        {
            (1, 2) => true, // Issued → Delivered
            (2, 3) => true, // Delivered → Archived
            _ => false,
        };

        return allowed
            ? Result.Ok()
            : Result.Fail(InvoicingErrors.InvalidCreditNoteTransition(Name, target.Name));
    }
}
