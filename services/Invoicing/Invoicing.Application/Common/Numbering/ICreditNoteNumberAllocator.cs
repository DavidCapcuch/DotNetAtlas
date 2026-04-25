using Invoicing.Domain.CreditNotes.ValueObjects;

namespace Invoicing.Application.Common.Numbering;

/// <summary>
/// Allocates the next gap-free <see cref="CreditNoteNumber"/> for the current
/// fiscal year (ADR-0018). Symmetric to
/// <see cref="IInvoiceNumberAllocator"/> but uses a separate allocator row so
/// invoice and credit-note sequences advance independently. See
/// <see cref="IInvoiceNumberAllocator"/> for the transactional contract.
/// </summary>
public interface ICreditNoteNumberAllocator
{
    /// <summary>
    /// Reserves and returns the next credit-note number for the current UTC
    /// year. Throws <see cref="Platform.SharedKernel.Exceptions.DataIntegrityException"/>
    /// if the allocated <see cref="CreditNoteNumber"/> rejects validation.
    /// </summary>
    Task<CreditNoteNumber> AllocateAsync(CancellationToken cancellationToken);
}
