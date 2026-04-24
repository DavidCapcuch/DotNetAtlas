using Ardalis.SmartEnum;

namespace Invoicing.Domain.Common.ValueObjects;

/// <summary>
/// Why a credit note was issued. v1 supports <see cref="OrderCancelled"/> only; the other
/// values are reserved slots for v2 (partial refunds, adjustments) per <c>invoicing.md § 17</c>.
/// </summary>
public sealed class CreditNoteReason : SmartEnum<CreditNoteReason>
{
    /// <summary>Order cancelled after payment capture \u2192 full-amount credit note. v1 primary.</summary>
    public static readonly CreditNoteReason OrderCancelled = new(nameof(OrderCancelled), 1);

    /// <summary>Partial refund \u2014 v2 only; v1 rejects with <c>PartialRefundNotSupportedV1</c>.</summary>
    public static readonly CreditNoteReason PartialRefund = new(nameof(PartialRefund), 2);

    /// <summary>Post-hoc adjustment (price correction, discount applied retroactively) \u2014 v2 only.</summary>
    public static readonly CreditNoteReason Adjustment = new(nameof(Adjustment), 3);

    private CreditNoteReason(string name, int value)
        : base(name, value)
    {
    }
}
