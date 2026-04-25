namespace Invoicing.Application.Common.Numbering;

/// <summary>
/// Persisted state for the gap-free credit-note-number allocator (ADR-0018).
/// Independent sequence from <see cref="InvoiceNumberAllocator"/>; one row per
/// fiscal year. Same row-lock semantics as the invoice allocator.
/// </summary>
public sealed class CreditNoteNumberAllocator
{
    /// <summary>Fiscal year identifying this allocator row (PK).</summary>
    public required short Year { get; set; }

    /// <summary>Next sequence value to hand out for <see cref="Year"/>; starts at 1.</summary>
    public required long NextValue { get; set; }

    /// <summary>Last-write timestamp; refreshed on every increment.</summary>
    public required DateTimeOffset UpdatedAt { get; set; }
}
