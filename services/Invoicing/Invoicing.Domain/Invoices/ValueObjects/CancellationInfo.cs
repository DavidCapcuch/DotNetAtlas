using Invoicing.Domain.Common.ValueObjects;
using Platform.SharedKernel.Base;

namespace Invoicing.Domain.Invoices.ValueObjects;

/// <summary>
/// Off-ramp metadata stamped onto a cancelled <c>Invoice</c> (I-6).
/// Immutable once set.
/// </summary>
/// <param name="CancelledAtUtc">When the cancellation transition was applied.</param>
/// <param name="Reason">Why the invoice was cancelled.</param>
/// <param name="CreditNoteId">The credit note that reverses this invoice.</param>
public sealed record CancellationInfo(
    DateTimeOffset CancelledAtUtc,
    CreditNoteReason Reason,
    Guid CreditNoteId) : ValueObject;
