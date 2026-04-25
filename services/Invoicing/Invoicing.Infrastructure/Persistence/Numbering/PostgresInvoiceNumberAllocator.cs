using System.Globalization;
using FluentResults;
using Invoicing.Application.Common.Numbering;
using Invoicing.Domain.Invoices.ValueObjects;
using Invoicing.Infrastructure.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.Exceptions;

namespace Invoicing.Infrastructure.Persistence.Numbering;

/// <summary>
/// Postgres adapter for <see cref="IInvoiceNumberAllocator"/> per ADR-0018.
/// Acquires a row-level lock on the per-year allocator row with
/// <c>SELECT ... FOR UPDATE</c>, increments <c>next_value</c>, and persists
/// the updated row inside the caller's transaction. The lock is released only
/// when that transaction commits or rolls back — making the sequence
/// gap-free under failure.
/// </summary>
internal sealed class PostgresInvoiceNumberAllocator : IInvoiceNumberAllocator
{
    private const string ExhaustedErrorCode = "Invoicing.InvoiceAllocatorExhausted";
    private const string InvariantErrorCode = "Invoicing.AllocatorInvariantViolation";
    private const string SequenceValidationCode = "Invoicing.InvalidInvoiceNumberSequence";

    private readonly InvoicingDbContext _db;
    private readonly TimeProvider _timeProvider;

    public PostgresInvoiceNumberAllocator(InvoicingDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<InvoiceNumber> AllocateAsync(CancellationToken cancellationToken)
    {
        // Hard-fail callers who forgot to open a transaction. Without one, the
        // FOR UPDATE row lock would auto-release per statement and the
        // gap-free invariant would not hold. Cheaper to detect here than to
        // discover the issue from a duplicate-number incident in production.
        if (_db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "InvoiceNumberAllocator.AllocateAsync requires an enclosing "
                + "transaction started via IInvoicingDbContext.Database.BeginTransactionAsync. "
                + "Without it, the FOR UPDATE row lock would release per-statement and "
                + "the gap-free invariant would not hold (ADR-0018).");
        }

        var year = (short)_timeProvider.GetUtcNow().Year;

        // The FOR UPDATE row lock participates in the caller's enclosing
        // transaction. Concurrent allocations for the same year serialize on
        // this row; rollback releases the lock without incrementing.
        var row = await _db.InvoiceNumberAllocators
            .FromSqlInterpolated(
                $"SELECT * FROM invoicing.invoice_number_allocator WHERE year = {year} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            // First issuance of a new fiscal year. Year-rollover concurrency
            // walk-through: two transactions A and B race on a non-existent
            // row. Both observe 0 rows from the initial FOR UPDATE; A inserts
            // first; B's INSERT blocks on A's index lock and after A commits
            // the ON CONFLICT clause makes B a no-op. Both then re-issue the
            // FOR UPDATE select and serialize on the now-existing row lock,
            // exactly as they would for any subsequent allocation in the
            // same year. No deadlock; A gets sequence 1, B gets sequence 2.
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO invoicing.invoice_number_allocator (year, next_value, updated_at)
                   VALUES ({year}, 1, now())
                   ON CONFLICT (year) DO NOTHING",
                cancellationToken).ConfigureAwait(false);

            row = await _db.InvoiceNumberAllocators
                .FromSqlInterpolated(
                    $"SELECT * FROM invoicing.invoice_number_allocator WHERE year = {year} FOR UPDATE")
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var sequence = row.NextValue;
        row.NextValue = sequence + 1;
        row.UpdatedAt = _timeProvider.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var result = InvoiceNumber.Create(year, sequence);
        if (result.IsFailed)
        {
            // Distinguish operational exhaustion (sequence > 999 999, an
            // actionable signal — rotate the format / shard the allocator)
            // from a structural invariant break.
            var code = IsSequenceExhaustion(result) ? ExhaustedErrorCode : InvariantErrorCode;
            throw new DataIntegrityException(
                code,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"InvoiceNumber.Create rejected year={year} sequence={sequence}: ")
                + string.Join("; ", result.Errors.Select(e => e.Message)));
        }

        return result.Value;
    }

    private static bool IsSequenceExhaustion(Result<InvoiceNumber> result) =>
        result.Errors.OfType<ValidationError>()
            .Any(e => string.Equals(e.ErrorCode, SequenceValidationCode, StringComparison.Ordinal));
}
