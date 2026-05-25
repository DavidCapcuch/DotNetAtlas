using AwesomeAssertions;
using Invoicing.Application.Common.Numbering;
using Invoicing.Domain.CreditNotes.ValueObjects;
using Invoicing.Infrastructure.Persistence.Database;
using Invoicing.Infrastructure.Persistence.Numbering;
using Invoicing.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Invoicing.IntegrationTests.Allocators;

/// <summary>
/// Integration tests for <see cref="ICreditNoteNumberAllocator"/>. The
/// rollback + year-rollover semantics are identical SQL to the invoice
/// allocator and are covered there; these tests prove the second adapter is
/// wired against the right table and produces the <c>CN-</c> prefix.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class CreditNoteNumberAllocatorTests
{
    private readonly IntegrationTestFixture _fixture;

    public CreditNoteNumberAllocatorTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HappyPath_FirstAllocation_ReturnsCnPrefixedYearOne_AndIncrementsNextValue()
    {
        var ct = TestContext.Current.CancellationToken;
        const int year = 2040;
        await ResetCreditNoteAllocatorAsync(year, nextValue: 1, ct);
        var clock = new FakeTimeProvider(new DateTimeOffset(year, 4, 1, 12, 0, 0, TimeSpan.Zero));

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        var allocator = new PostgresCreditNoteNumberAllocator(db, clock);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var number = await allocator.AllocateAsync(ct);
        await tx.CommitAsync(ct);

        number.Value.Should().Be($"CN-{year:D4}-000001");

        await AssertCreditNoteNextValueAsync(year, expected: 2, ct);
    }

    [Fact]
    public async Task Concurrent_NTasks_ReceiveDistinctConsecutiveNumbers_NoDuplicates()
    {
        var ct = TestContext.Current.CancellationToken;
        const int year = 2041;
        const long startSequence = 50;
        const int parallelism = 16;
        await ResetCreditNoteAllocatorAsync(year, nextValue: startSequence, ct);
        var initial = new DateTimeOffset(year, 4, 1, 12, 0, 0, TimeSpan.Zero);

        var tasks = Enumerable.Range(0, parallelism)
            .Select(_ => AllocateInOwnScopeAsync(initial, ct))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        var values = results.Select(n => n.Value).ToList();
        var expected = Enumerable.Range(0, parallelism)
            .Select(i =>
            {
                var seq = startSequence + i;
                return $"CN-{year:D4}-{seq:D6}";
            })
            .ToList();
        values.Should().BeEquivalentTo(expected);
        values.Should().OnlyHaveUniqueItems();

        await AssertCreditNoteNextValueAsync(year, expected: startSequence + parallelism, ct);
    }

    private async Task<CreditNoteNumber> AllocateInOwnScopeAsync(DateTimeOffset clockMoment, CancellationToken ct)
    {
        var clock = new FakeTimeProvider(clockMoment);
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        var allocator = new PostgresCreditNoteNumberAllocator(db, clock);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var number = await allocator.AllocateAsync(ct);
        await tx.CommitAsync(ct);
        return number;
    }

    private async Task ResetCreditNoteAllocatorAsync(int year, long nextValue, CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();

        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO invoicing.credit_note_number_allocator (year, next_value, updated_at)
               VALUES ({(short)year}, {nextValue}, now())
               ON CONFLICT (year) DO UPDATE SET next_value = EXCLUDED.next_value, updated_at = now()",
            ct);
    }

    private async Task AssertCreditNoteNextValueAsync(int year, long expected, CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        var row = await db.CreditNoteNumberAllocators
            .AsNoTracking()
            .SingleAsync(r => r.Year == (short)year, ct);
        row.NextValue.Should().Be(expected);
    }
}
