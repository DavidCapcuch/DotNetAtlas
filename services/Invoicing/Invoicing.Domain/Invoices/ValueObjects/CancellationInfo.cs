using Invoicing.Domain.Common.ValueObjects;
using Platform.SharedKernel.Base;

namespace Invoicing.Domain.Invoices.ValueObjects;

/// <summary>
/// Off-ramp metadata stamped onto a cancelled <c>Invoice</c> (I-6).
/// Immutable once set.
/// </summary>
public sealed record CancellationInfo : ValueObject
{
    /// <summary>When the cancellation transition was applied.</summary>
    public DateTimeOffset CancelledAtUtc { get; private init; }

    /// <summary>Why the invoice was cancelled.</summary>
    public CreditNoteReason Reason { get; private init; } = null!;

    /// <summary>The credit note that reverses this invoice.</summary>
    public Guid CreditNoteId { get; private init; }

    private CancellationInfo()
    {
    }

    public static CancellationInfo Create(DateTimeOffset cancelledAtUtc, CreditNoteReason reason, Guid creditNoteId) =>
        new()
        {
            CancelledAtUtc = cancelledAtUtc,
            Reason = reason,
            CreditNoteId = creditNoteId,
        };
}
