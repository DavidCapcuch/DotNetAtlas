using System.Reflection;
using System.Runtime.CompilerServices;
using Platform.SharedKernel.Base.DomainEvents;

namespace Platform.SharedKernel.UnitTests.Base.DomainEvents;

public class DomainEventTests
{
    [Fact]
    public void OccurredOnUtc_IsRequired_NoWallClockDefault()
    {
        var prop = typeof(DomainEvent).GetProperty(
            nameof(DomainEvent.OccurredOnUtc),
            BindingFlags.Public | BindingFlags.Instance)!;

        prop.GetCustomAttribute<RequiredMemberAttribute>().Should().NotBeNull(
            "ADR-0015 forbids wall-clock fallback; callers must inject TimeProvider.GetUtcNow()");
    }

    [Fact]
    public void TestEvent_ConstructedWithExplicitTimestamp_RoundTrips()
    {
        var ts = new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero);
        var e = new TestEvent { OccurredOnUtc = ts };
        e.OccurredOnUtc.Should().Be(ts);
    }

    // Defensive sentinel for #138: enumerate every concrete DomainEvent subtype reachable from
    // the platform-test reference graph and assert each inherits a `[RequiredMember]`-decorated
    // OccurredOnUtc. Catches accidental removal of the `required` modifier on the base — the
    // cross-BC compile-time guarantee remains a solution-wide `dotnet build -m`: a slice build
    // (Domain-only / one-BC-only) does not surface CS9035 in downstream BC trees.
    [Fact]
    public void OccurredOnUtc_RemainsRequiredOnEverySubtype()
    {
        var subtypes = typeof(DomainEvent).Assembly
            .GetTypes()
            .Concat(typeof(DomainEventTests).Assembly.GetTypes())
            .Where(t => !t.IsAbstract && typeof(DomainEvent).IsAssignableFrom(t))
            .ToArray();

        using var scope = new AssertionScope();
        foreach (var t in subtypes)
        {
            var prop = t.GetProperty(
                nameof(DomainEvent.OccurredOnUtc),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

            prop.Should().NotBeNull($"{t.FullName} should expose OccurredOnUtc");
            prop!.GetCustomAttribute<RequiredMemberAttribute>().Should().NotBeNull(
                $"{t.FullName}.OccurredOnUtc must remain a `required` member (ADR-0015)");
        }
    }

    private sealed record TestEvent : DomainEvent;
}
