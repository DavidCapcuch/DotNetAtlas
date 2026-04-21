# ADR-0018: Invoice Numbering — Transactional Gap-Free Allocator

## Status

Accepted (2026-04-19)

## Context

Invoicing issues legally-binding fiscal documents. EU VAT law (and equivalent tax regimes in many jurisdictions) typically mandates that invoice numbers be:

- **Sequential** — each number is larger than the previous one.
- **Gap-free** — no number is skipped. An auditor presented with `INV-2026-000142` after `INV-2026-000140` has a missing invoice to account for.
- **Scoped by year (or other fiscal boundary)** — most jurisdictions allow a fresh sequence each fiscal year (sometimes with a reset to `INV-2027-000001`).

The naive approach in Postgres is a `SEQUENCE`:

```sql
CREATE SEQUENCE invoice_number_seq START 1;
SELECT nextval('invoice_number_seq');  -- returns 142
```

This fails the gap-free requirement. Postgres sequences intentionally do **not** participate in transactions: if the transaction that called `nextval(142)` rolls back, the sequence stays at 142. The next successful call returns 143. Number 142 is gone forever. For fiscal records this is not acceptable.

The Invoicing BC ([invoicing.md](../bc-design/invoicing.md)) requires gap-free numbering. This ADR commits to the implementation.

## Decision Drivers (ranked)

1. **Gap-free guarantee** — the allocation must release the number if the transaction rolls back.
2. **Correctness under concurrency** — two simultaneous invoice issuances must not both get the same number.
3. **Adequate throughput** — the design must not choke at the reference-solution profile (≤ 50 rps; invoicing is downstream of captured payments — effective rate closer to ≤ 5 issuances/sec).
4. **Year rollover correctness** — transitioning from 2026 → 2027 must reset the sequence without violating invariants.
5. **Teachable** — the pattern must be understandable in a 15-minute read, not require a database-internals expert.

## Considered Options

### Option 1: Transactional allocator table — `SELECT ... FOR UPDATE` + UPDATE inside issuing transaction

A dedicated table `invoicing.invoice_number_allocator(year int PK, next_value bigint)`. Inside the `IssueInvoiceCommand` transaction:

```sql
SELECT next_value FROM invoicing.invoice_number_allocator
  WHERE year = EXTRACT(YEAR FROM NOW())
  FOR UPDATE;
-- format as INV-2026-000142
UPDATE invoicing.invoice_number_allocator SET next_value = next_value + 1
  WHERE year = EXTRACT(YEAR FROM NOW());
-- INSERT INTO invoices (...);
COMMIT;  -- number is now committed; rollback would release the lock without incrementing
```

Row-level lock serializes concurrent issuances on the same year. Rollback releases the lock cleanly. No gap.

### Option 2: Hi/Lo allocator (batch-fetch + local increment)

Allocate a block of N numbers at once; hand them out from memory; checkpoint back to DB when the block is exhausted. Used by NHibernate and EF Core for `SERIAL`-like pooling.

### Option 3: Reserve-at-start, renumber-on-commit

Issue a temporary placeholder number at transaction start; renumber at commit time under a global lock. Two-phase approach.

### Option 4: Accept gaps and document as v1 limitation

Use a regular `SEQUENCE`. Document the gap as a v1 known issue; production adopters swap to Option 1.

## Evaluation Matrix

| Driver (ranked) | Option 1: Allocator + FOR UPDATE | Option 2: Hi/Lo | Option 3: Reserve + renumber | Option 4: Accept gaps |
|---|---|---|---|---|
| 1. Gap-free guarantee | Yes — rollback releases row lock without increment | No — exhausted block = gap; unclean shutdown = big gap | Hard — renumber at commit creates new race conditions | Explicit failure |
| 2. Correctness under concurrency | Yes — row-level lock serializes | Yes (per block) | Complex; commit-lock itself contentious | N/A |
| 3. Throughput | ~1 issuance per transaction RTT on the allocator row | Much higher (in-memory increments) | Lower than Option 1 due to commit-lock | Highest |
| 4. Year rollover | Natural — new row for new year; upsert on first issuance | Complex — in-memory block must flush pre-rollover | Complex | Irrelevant |
| 5. Teachable | 15-minute read — pattern matches what a Postgres DBA would do manually | Requires explaining in-memory state management | Two-phase commit-like complexity | Trivial |

## Decision

We will use **Option 1: transactional allocator table with `SELECT ... FOR UPDATE`** as the gap-free invoice-number mechanism. Same pattern applied to credit notes (`credit_note_number_allocator`).

## Rationale

Option 1 is what a database textbook would prescribe. The row-level lock is exactly the primitive that `SEQUENCE` lacks: it participates in the enclosing transaction, so rollback cleanly releases it without incrementing. Concurrency is handled by Postgres — the second transaction waits on the row lock and only proceeds after the first commits or rolls back. Year rollover is natural: a new year is a new row (UPSERT on first issuance).

Throughput is the only real concern. The allocator row serializes all issuances for a given year. At reference-solution scale (≤ 5 issuances/sec for the year's invoices) this is easy. Production adopters with higher throughput can shard by year-and-month (e.g., `year_month` PK with format `INV-YYYY-MM-NNNNN`) or adopt a Hi/Lo variant — documented in § Future scaling.

Option 2 (Hi/Lo) is what EF Core uses for `SERIAL`-backed IDs — but it trades gap-free for throughput. The reference solution explicitly targets gap-free; teaching Hi/Lo here would teach the wrong lesson. Option 3 is over-engineered. Option 4 is dishonest about fiscal-record requirements.

## Consequences

### Positive

- **Gap-free guaranteed** — rollback releases the lock without increment. Invariants `I-3` (immutable numbers) and the fiscal-law requirement hold without further machinery.
- **Correctness under concurrency** — Postgres row lock does the work; no app-level coordination needed.
- **Year rollover** is a normal UPSERT — no special-case code.
- **Teachable pattern** — readers learn the exact technique a DBA would recommend: "FOR UPDATE on an allocator row inside the issuing transaction".
- **Symmetric treatment for credit notes** — same pattern, separate table.

### Negative

- **Serialized per-year** — all issuances for a given year serialize on one DB row. At reference scale, trivial. At very high volume (100+ issuances/sec), requires sharding (see § Future scaling).
- **Long-running issuing transaction holds the lock** — if the PDF upload to Azurite/Azure Blob takes 2s, the allocator lock is held for 2s. Mitigation: PDF generation is fast (QuestPDF); upload is measured in tens of ms; the 2-second pathological case is rare.
- **Deadlock risk on failed issuance that retries** — extremely unlikely because only one allocator row is involved; lock order is the same on every call.

### Risks

- **Hot-year-row under peak load** — end-of-year rush could bottleneck. Mitigation: month-shard variant is a drop-in replacement (§ Future scaling).
- **Operator manually updates `next_value`** — someone runs an UPDATE to "skip ahead" to align numbers with an external system. This violates gap-free. Mitigation: an architecture-test-equivalent audit: a nightly job verifies `COUNT(invoices WHERE year = 2026) == invoice_number_allocator.next_value - 1` and alerts on mismatch.
- **Year-boundary race** — an invoice issued at 2026-12-31T23:59:59.999 and another at 2027-01-01T00:00:00.001 race for different years. Postgres `NOW()` resolution is sufficient; the allocator lookup uses `EXTRACT(YEAR FROM NOW())` which is deterministic per transaction.

## Implementation Notes

### Schema

```sql
CREATE SCHEMA IF NOT EXISTS invoicing;

CREATE TABLE invoicing.invoice_number_allocator (
    year smallint PRIMARY KEY,
    next_value bigint NOT NULL CHECK (next_value >= 1),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE invoicing.credit_note_number_allocator (
    year smallint PRIMARY KEY,
    next_value bigint NOT NULL CHECK (next_value >= 1),
    updated_at timestamptz NOT NULL DEFAULT now()
);
```

Seed on service startup: `INSERT ... ON CONFLICT DO NOTHING` for the current year with `next_value = 1`.

### Allocation flow (pseudocode inside `IssueInvoiceCommandHandler`)

```csharp
await using var tx = await _db.Database.BeginTransactionAsync(ct);

// 1. Lock + read
var allocator = await _db.InvoiceNumberAllocators
    .FromSqlInterpolated($"SELECT * FROM invoicing.invoice_number_allocator WHERE year = {currentYear} FOR UPDATE")
    .SingleOrDefaultAsync(ct);

if (allocator is null)
{
    // Year rollover — insert
    _db.InvoiceNumberAllocators.Add(new InvoiceNumberAllocator { Year = currentYear, NextValue = 1 });
    await _db.SaveChangesAsync(ct);
    allocator = await _db.InvoiceNumberAllocators
        .FromSqlInterpolated($"SELECT * FROM invoicing.invoice_number_allocator WHERE year = {currentYear} FOR UPDATE")
        .SingleAsync(ct);
}

// 2. Generate the number
var invoiceNumber = InvoiceNumber.Create(currentYear, allocator.NextValue);  // "INV-2026-000142"

// 3. Increment (committed with the rest of the transaction)
allocator.NextValue += 1;
allocator.UpdatedAt = _clock.UtcNow;

// 4. Create Invoice aggregate, upload PDF, persist
var invoice = Invoice.Create(invoiceNumber, ...);
_db.Invoices.Add(invoice);
await _blobStore.UploadAsync(...);
_db.OutboxMessages.Add(new OutboxMessage(...));

await _db.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
// On exception / rollback, the FOR UPDATE lock releases without incrementing
```

**Critical detail:** the `FOR UPDATE` row lock is held until the transaction commits or rolls back. Other transactions attempting to allocate for the same year wait on the lock, then observe the incremented `next_value`.

### Number formatting

```csharp
public sealed record InvoiceNumber(string Value)
{
    public static InvoiceNumber Create(int year, long sequence) =>
        new($"INV-{year:0000}-{sequence:000000}");  // INV-2026-000142
}
```

- `{year:0000}` — 4-digit year
- `{sequence:000000}` — 6-digit zero-padded sequence

Credit notes: `CN-{year:0000}-{sequence:000000}` — separate format, separate allocator, separate sequence starting at 1 each year.

### Integration-test coverage

- **Happy path** — single issuance increments allocator by 1.
- **Rollback preserves allocator** — force handler to fail mid-transaction (throw after allocator UPDATE but before commit); assert `next_value` unchanged.
- **Concurrency** — two tasks issue simultaneously; assert consecutive numbers, no duplicates.
- **Year rollover** — use `FakeTimeProvider` to cross 2026-12-31 → 2027-01-01; assert new allocator row for 2027 with `next_value = 1`.

### Audit integrity check

Nightly job (or reference-solution integration test run):

```sql
SELECT year, COUNT(*) as issued, MAX(allocator.next_value - 1) as expected
FROM invoicing.invoices JOIN invoicing.invoice_number_allocator allocator USING (year)
GROUP BY year
HAVING COUNT(*) != MAX(allocator.next_value - 1);  -- must be empty
```

If this query returns rows, an operator has manually modified `next_value` — runbook dictates investigation.

### Future scaling (documented, not implemented in v1)

At > 100 issuances/sec for a single year, the allocator row becomes contended. Options:

- **Month-shard**: `year_month` PK (`202604`), format `INV-2026-04-000142`. Reduces contention 12x; changes the number format.
- **Hi/Lo variant**: allocate blocks of 100 numbers per transaction; hand out from memory. Gap-free iff clean shutdown flushes the unused tail — production-grade Hi/Lo implementations write a "used-up" marker when truncating the block.
- **Separate allocator per high-volume tenant / jurisdiction** — if multi-tenant, each tenant's invoice numbering is independent.

None of these are needed at the reference-solution profile.

## Related Decisions

- [ADR-0009: Reference-Solution Target Profile](0009-reference-solution-target-profile.md) — throughput budget for serialized allocation
- [ADR-0011: PII Handling & GDPR](0011-pii-handling-gdpr.md) — invoice numbers are NOT PII; survive erasure
- [ADR-0015: Time & Timezone Policy](0015-time-timezone-policy.md) — year derived from `TimeProvider.GetUtcNow()` for testability
- [ADR-0017: Blob Storage + CDN](0017-blob-storage-cdn.md) — PDF upload happens inside the same transaction that allocates the number
- [ADR-0019: PDF Generation (QuestPDF)](0019-pdf-generation-questpdf.md) — produces the document that bears the allocated number
