using Invoicing.Domain.Invoices.ValueObjects;

namespace Invoicing.Application.Common.Numbering;

/// <summary>
/// Allocates the next gap-free <see cref="InvoiceNumber"/> for the current
/// fiscal year (ADR-0018).
/// </summary>
/// <remarks>
/// <para>
/// The caller MUST own the enclosing transaction
/// (<c>IInvoicingDbContext.Database.BeginTransactionAsync</c>). The
/// implementation acquires a Postgres row-level lock on the allocator row
/// (<c>SELECT ... FOR UPDATE</c>) and atomically increments
/// <see cref="InvoiceNumberAllocator.NextValue"/> in the same transaction. The
/// lock is held until the caller commits or rolls back: rollback releases the
/// lock without incrementing, which is precisely the property that keeps the
/// sequence gap-free under failure.
/// </para>
/// <para>
/// Year derivation uses <see cref="System.TimeProvider.GetUtcNow"/> per
/// ADR-0015 so tests can cross fiscal-year boundaries deterministically with
/// <c>FakeTimeProvider</c>.
/// </para>
/// </remarks>
public interface IInvoiceNumberAllocator
{
    /// <summary>
    /// Reserves and returns the next invoice number for the current UTC year.
    /// Throws <see cref="Platform.SharedKernel.Exceptions.DataIntegrityException"/>
    /// if the allocated <see cref="InvoiceNumber"/> rejects validation (e.g.
    /// the per-year sequence is exhausted at 999 999).
    /// </summary>
    Task<InvoiceNumber> AllocateAsync(CancellationToken cancellationToken);
}
