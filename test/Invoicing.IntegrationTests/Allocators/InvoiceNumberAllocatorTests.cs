using AwesomeAssertions;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Numbering;
using Invoicing.Domain.Invoices.ValueObjects;
using Invoicing.Infrastructure.Persistence.Database;
using Invoicing.Infrastructure.Persistence.Numbering;
using Invoicing.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Invoicing.IntegrationTests.Allocators;

/// <summary>
/// Integration tests for <see cref="IInvoiceNumberAllocator"/> proving the
/// four ADR-0018 properties: gap-free under success, gap-free under
/// rollback, serialization under concurrency, and clean year-rollover.
/// Each test instantiates its own <see cref="FakeTimeProvider"/> so the
/// fixture's clock never goes backwards across test orderings.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class InvoiceNumberAllocatorTests
{
    private readonly IntegrationTestFixture _fixture;

    public InvoiceNumberAllocatorTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HappyPath_FirstAllocation_ReturnsYearOne_AndIncrementsNextValueToTwo()
    {
        var ct = TestContext.Current.CancellationToken;
        const int year = 2030;
        await ResetInvoiceAllocatorAsync(year, nextValue: 1, ct);
        var clock = new FakeTimeProvider(new DateTimeOffset(year, 4, 1, 12, 0, 0, TimeSpan.Zero));

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        var allocator = new PostgresInvoiceNumberAllocator(db, clock);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var number = await allocator.AllocateAsync(ct);
        await tx.CommitAsync(ct);

        number.Value.Should().Be($"INV-{year:D4}-000001");

        await AssertInvoiceNextValueAsync(year, expected: 2, ct);
    }

    [Fact]
    public async Task Rollback_PreservesAllocator_NoIncrement()
    {
        var ct = TestContext.Current.CancellationToken;
        const int year = 2031;
        await ResetInvoiceAllocatorAsync(year, nextValue: 42, ct);
        var clock = new FakeTimeProvider(new DateTimeOffset(year, 4, 1, 12, 0, 0, TimeSpan.Zero));

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        var allocator = new PostgresInvoiceNumberAllocator(db, clock);

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var number = await allocator.AllocateAsync(ct);
            number.Value.Should().Be($"INV-{year:D4}-000042");

            // Simulate a downstream failure inside the issuing transaction.
            await tx.RollbackAsync(ct);
        }

        // Gap-free guarantee: the rolled-back allocation must NOT have
        // incremented next_value.
        await AssertInvoiceNextValueAsync(year, expected: 42, ct);
    }

    [Fact]
    public async Task Concurrent_NTasks_ReceiveDistinctConsecutiveNumbers_NoDuplicates()
    {
        var ct = TestContext.Current.CancellationToken;
        const int year = 2032;
        const long startSequence = 100;
        const int parallelism = 16;
        await ResetInvoiceAllocatorAsync(year, nextValue: startSequence, ct);
        var initial = new DateTimeOffset(year, 4, 1, 12, 0, 0, TimeSpan.Zero);

        // Each task gets its own DI scope + DbContext + connection so the
        // FOR UPDATE row lock can actually serialize them. Sharing a single
        // DbContext across tasks would just serialize through ChangeTracker.
        // We use N=16 parallel tasks because two-task concurrency cannot
        // distinguish "lock serialised correctly" from "tasks happened to
        // run sequentially". With 16 parallel issuances, the probability of
        // 16 distinct numbers without lock serialisation is negligible —
        // the absence of FOR UPDATE would surface as duplicates almost
        // every run.
        var tasks = Enumerable.Range(0, parallelism)
            .Select(_ => AllocateInOwnScopeAsync(initial, ct))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        var values = results.Select(n => n.Value).ToList();
        var expected = Enumerable.Range(0, parallelism)
            .Select(i =>
            {
                var seq = startSequence + i;
                return $"INV-{year:D4}-{seq:D6}";
            })
            .ToList();
        values.Should().BeEquivalentTo(expected);
        values.Should().OnlyHaveUniqueItems();

        await AssertInvoiceNextValueAsync(year, expected: startSequence + parallelism, ct);
    }

    [Fact]
    public async Task YearRollover_AdvanceClockToNextYear_StartsAtOne_AndOldYearAllocatorUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        const int oldYear = 2033;
        const int newYear = 2034;
        await ResetInvoiceAllocatorAsync(oldYear, nextValue: 1, ct);
        await DeleteInvoiceAllocatorRowAsync(newYear, ct);

        var clock = new FakeTimeProvider(new DateTimeOffset(oldYear, 12, 31, 23, 59, 0, TimeSpan.Zero));

        // Allocate once on 2033-12-31.
        await AllocateWithClockAsync(clock, ct);

        // Advance into 2034.
        clock.SetUtcNow(new DateTimeOffset(newYear, 1, 1, 0, 0, 1, TimeSpan.Zero));
        var firstOfNewYear = await AllocateWithClockAsync(clock, ct);

        firstOfNewYear.Value.Should().Be($"INV-{newYear:D4}-000001");

        // Old-year allocator advanced to 2 (one issuance). New-year allocator
        // must exist with next_value = 2 (one issuance), independent of old.
        await AssertInvoiceNextValueAsync(oldYear, expected: 2, ct);
        await AssertInvoiceNextValueAsync(newYear, expected: 2, ct);
    }

    private async Task<InvoiceNumber> AllocateInOwnScopeAsync(DateTimeOffset clockMoment, CancellationToken ct)
    {
        var clock = new FakeTimeProvider(clockMoment);
        return await AllocateWithClockAsync(clock, ct);
    }

    private async Task<InvoiceNumber> AllocateWithClockAsync(FakeTimeProvider clock, CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        var allocator = new PostgresInvoiceNumberAllocator(db, clock);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var number = await allocator.AllocateAsync(ct);
        await tx.CommitAsync(ct);
        return number;
    }

    private async Task ResetInvoiceAllocatorAsync(int year, long nextValue, CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();

        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO invoicing.invoice_number_allocator (year, next_value, updated_at)
               VALUES ({(short)year}, {nextValue}, now())
               ON CONFLICT (year) DO UPDATE SET next_value = EXCLUDED.next_value, updated_at = now()",
            ct);
    }

    private async Task DeleteInvoiceAllocatorRowAsync(int year, CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM invoicing.invoice_number_allocator WHERE year = {(short)year}",
            ct);
    }

    private async Task AssertInvoiceNextValueAsync(int year, long expected, CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        var row = await db.InvoiceNumberAllocators
            .AsNoTracking()
            .SingleAsync(r => r.Year == (short)year, ct);
        row.NextValue.Should().Be(expected);
    }
}
