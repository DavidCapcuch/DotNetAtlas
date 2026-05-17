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

    private sealed record TestEvent : DomainEvent;
}
