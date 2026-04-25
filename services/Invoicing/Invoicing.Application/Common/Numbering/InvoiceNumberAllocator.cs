namespace Invoicing.Application.Common.Numbering;

/// <summary>
/// Persisted state for the gap-free invoice-number allocator (ADR-0018).
/// One row per fiscal year. Lives in the Application layer because the
/// <see cref="Data.IInvoicingDbContext"/> port surfaces a typed
/// <c>DbSet&lt;InvoiceNumberAllocator&gt;</c>; mapping + table layout is owned by
/// the Infrastructure entity configuration.
/// </summary>
/// <remarks>
/// The row is mutated under <c>SELECT ... FOR UPDATE</c> inside the issuing
/// transaction. Rollback releases the row lock without incrementing
/// <see cref="NextValue"/>, which is exactly what makes the sequence gap-free.
/// </remarks>
public sealed class InvoiceNumberAllocator
{
    /// <summary>Fiscal year identifying this allocator row (PK).</summary>
    public required short Year { get; set; }

    /// <summary>Next sequence value to hand out for <see cref="Year"/>; starts at 1.</summary>
    public required long NextValue { get; set; }

    /// <summary>Last-write timestamp; refreshed on every increment.</summary>
    public required DateTimeOffset UpdatedAt { get; set; }
}
