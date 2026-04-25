using System.Globalization;
using FluentResults;
using Invoicing.Application.Common.Numbering;
using Invoicing.Domain.CreditNotes.ValueObjects;
using Invoicing.Infrastructure.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.Exceptions;

namespace Invoicing.Infrastructure.Persistence.Numbering;

/// <summary>
/// Postgres adapter for <see cref="ICreditNoteNumberAllocator"/> per ADR-0018.
/// Same row-lock semantics as <see cref="PostgresInvoiceNumberAllocator"/> but
/// targets the <c>credit_note_number_allocator</c> table so the two sequences
/// advance independently.
/// </summary>
internal sealed class PostgresCreditNoteNumberAllocator : ICreditNoteNumberAllocator
{
    private const string ExhaustedErrorCode = "Invoicing.CreditNoteAllocatorExhausted";
    private const string InvariantErrorCode = "Invoicing.AllocatorInvariantViolation";
    private const string SequenceValidationCode = "Invoicing.InvalidCreditNoteNumberSequence";

    private readonly InvoicingDbContext _db;
    private readonly TimeProvider _timeProvider;

    public PostgresCreditNoteNumberAllocator(InvoicingDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<CreditNoteNumber> AllocateAsync(CancellationToken cancellationToken)
    {
        if (_db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "CreditNoteNumberAllocator.AllocateAsync requires an enclosing "
                + "transaction started via IInvoicingDbContext.Database.BeginTransactionAsync. "
                + "Without it, the FOR UPDATE row lock would release per-statement and "
                + "the gap-free invariant would not hold (ADR-0018).");
        }

        var year = (short)_timeProvider.GetUtcNow().Year;

        var row = await _db.CreditNoteNumberAllocators
            .FromSqlInterpolated(
                $"SELECT * FROM invoicing.credit_note_number_allocator WHERE year = {year} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            // Year-rollover concurrency: see PostgresInvoiceNumberAllocator
            // for the walk-through; same INSERT ... ON CONFLICT DO NOTHING +
            // re-select FOR UPDATE pattern. No deadlock, monotonic outcome.
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO invoicing.credit_note_number_allocator (year, next_value, updated_at)
                   VALUES ({year}, 1, now())
                   ON CONFLICT (year) DO NOTHING",
                cancellationToken).ConfigureAwait(false);

            row = await _db.CreditNoteNumberAllocators
                .FromSqlInterpolated(
                    $"SELECT * FROM invoicing.credit_note_number_allocator WHERE year = {year} FOR UPDATE")
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var sequence = row.NextValue;
        row.NextValue = sequence + 1;
        row.UpdatedAt = _timeProvider.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var result = CreditNoteNumber.Create(year, sequence);
        if (result.IsFailed)
        {
            var code = IsSequenceExhaustion(result) ? ExhaustedErrorCode : InvariantErrorCode;
            throw new DataIntegrityException(
                code,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"CreditNoteNumber.Create rejected year={year} sequence={sequence}: ")
                + string.Join("; ", result.Errors.Select(e => e.Message)));
        }

        return result.Value;
    }

    private static bool IsSequenceExhaustion(Result<CreditNoteNumber> result) =>
        result.Errors.OfType<ValidationError>()
            .Any(e => string.Equals(e.ErrorCode, SequenceValidationCode, StringComparison.Ordinal));
}
