namespace Weather.UnitTests.Common;

/// <summary>
/// Fixed-instant constants for unit tests that need a deterministic <see cref="DateTimeOffset"/>
/// — typically to satisfy the <c>DateTimeOffset utcNow</c> parameter that flows into the
/// <c>DomainEvent.OccurredOnUtc</c> required member (ADR-0015). Tests asserting on the value
/// itself should declare their own local instant; this constant is for the many sites where
/// the value is irrelevant to the assertion.
/// </summary>
internal static class TestInstants
{
    public static readonly DateTimeOffset FixedNow = new(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);
}
