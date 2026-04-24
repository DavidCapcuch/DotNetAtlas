using Ardalis.SmartEnum;
using FluentResults;
using Invoicing.Domain.Common.Errors;

namespace Invoicing.Domain.Invoices.ValueObjects;

/// <summary>
/// FSM states for <c>Invoice</c> per <c>invoicing.md § 4</c>.
/// Valid transitions: <c>Draft \u2192 Issued \u2192 Delivered \u2192 Archived</c>; <c>Cancelled</c> is
/// an off-ramp from <see cref="Draft"/> or <see cref="Issued"/>/<see cref="Delivered"/>.
/// </summary>
public sealed class InvoiceStatus : SmartEnum<InvoiceStatus>
{
    public static readonly InvoiceStatus Draft = new(nameof(Draft), 1);
    public static readonly InvoiceStatus Issued = new(nameof(Issued), 2);
    public static readonly InvoiceStatus Delivered = new(nameof(Delivered), 3);
    public static readonly InvoiceStatus Archived = new(nameof(Archived), 4);
    public static readonly InvoiceStatus Cancelled = new(nameof(Cancelled), 5);

    private InvoiceStatus(string name, int value)
        : base(name, value)
    {
    }

    /// <summary>
    /// Returns <see cref="Result.Ok()"/> if the transition is allowed by the FSM, otherwise a
    /// <c>InvoicingErrors.InvalidTransition</c> validation error. Invariant I-5.
    /// </summary>
    public Result CanTransitionTo(InvoiceStatus target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var allowed = (Value, target.Value) switch
        {
            // Draft → Issued or Cancelled
            (1, 2) => true,
            (1, 5) => true,

            // Issued → Delivered or Cancelled
            (2, 3) => true,
            (2, 5) => true,

            // Delivered → Archived or Cancelled
            (3, 4) => true,
            (3, 5) => true,

            _ => false,
        };

        return allowed
            ? Result.Ok()
            : Result.Fail(InvoicingErrors.InvalidInvoiceTransition(Name, target.Name));
    }
}
