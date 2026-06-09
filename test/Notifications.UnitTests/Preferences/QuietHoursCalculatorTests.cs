using AwesomeAssertions;
using Notifications.Domain.Preferences;
using Xunit;

namespace Notifications.UnitTests.Preferences;

/// <summary>
/// Unit coverage for the pure quiet-hours scheduling rule (notifications.md § 5.4, ADR-0015):
/// deterministic instants in <c>Europe/Prague</c>, including the midnight-wrap window the seed
/// demonstrates (22:00–07:00) and both DST transitions.
/// </summary>
public sealed class QuietHoursCalculatorTests
{
    private const string Prague = "Europe/Prague";

    private static readonly TimeOnly QuietStart = new(22, 0);
    private static readonly TimeOnly QuietEnd = new(7, 0);

    [Fact]
    public void NextAllowedUtc_OutsideQuietWindow_ReturnsNowUnchanged()
    {
        // Arrange — 2026-06-09 12:00 Europe/Prague (CEST, +02:00) = 10:00Z: midday, well outside 22:00–07:00.
        var nowUtc = new DateTimeOffset(2026, 6, 9, 10, 0, 0, TimeSpan.Zero);

        // Act
        var result = QuietHoursCalculator.NextAllowedUtc(nowUtc, QuietStart, QuietEnd, Prague);

        // Assert
        result.Should().Be(nowUtc);
    }

    [Fact]
    public void NextAllowedUtc_InsideSameDayWindow_DefersToTheWindowEndInUtc()
    {
        // Arrange — non-wrapping window 13:00–15:00; 2026-06-09 14:00 CEST = 12:00Z is inside.
        var nowUtc = new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);

        // Act
        var result = QuietHoursCalculator.NextAllowedUtc(
            nowUtc, new TimeOnly(13, 0), new TimeOnly(15, 0), Prague);

        // Assert — 15:00 CEST the same local day = 13:00Z.
        result.Should().Be(new DateTimeOffset(2026, 6, 9, 13, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void NextAllowedUtc_NoQuietHours_ReturnsNowUnchanged()
    {
        // Arrange — 23:30 CEST: would be deep inside any 22:00–07:00 window, but the recipient has none.
        var nowUtc = new DateTimeOffset(2026, 6, 9, 21, 30, 0, TimeSpan.Zero);

        // Act
        var result = QuietHoursCalculator.NextAllowedUtc(nowUtc, quietStart: null, quietEnd: null, Prague);

        // Assert
        result.Should().Be(nowUtc);
    }

    [Fact]
    public void NextAllowedUtc_ExactlyAtWindowStart_IsInside()
    {
        // Arrange — [start, end) is half-open: 22:00:00 CEST sharp = 20:00Z is the first quiet instant.
        var nowUtc = new DateTimeOffset(2026, 6, 9, 20, 0, 0, TimeSpan.Zero);

        // Act
        var result = QuietHoursCalculator.NextAllowedUtc(nowUtc, QuietStart, QuietEnd, Prague);

        // Assert — deferred to 2026-06-10 07:00 CEST = 05:00Z.
        result.Should().Be(new DateTimeOffset(2026, 6, 10, 5, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void NextAllowedUtc_ExactlyAtWindowEnd_IsOutside()
    {
        // Arrange — 07:00:00 CEST sharp = 05:00Z: the window is [start, end), so its end instant is allowed.
        var nowUtc = new DateTimeOffset(2026, 6, 10, 5, 0, 0, TimeSpan.Zero);

        // Act
        var result = QuietHoursCalculator.NextAllowedUtc(nowUtc, QuietStart, QuietEnd, Prague);

        // Assert
        result.Should().Be(nowUtc);
    }

    [Fact]
    public void NextAllowedUtc_MidnightWrapBeforeMidnight_DefersToTheNextLocalDayEnd()
    {
        // Arrange — 2026-06-09 23:00 CEST = 21:00Z: inside the wrapped 22:00–07:00 window, before
        // midnight, so the window ends on the NEXT local day (06-10 07:00 CEST = 05:00Z).
        var nowUtc = new DateTimeOffset(2026, 6, 9, 21, 0, 0, TimeSpan.Zero);

        // Act
        var result = QuietHoursCalculator.NextAllowedUtc(nowUtc, QuietStart, QuietEnd, Prague);

        // Assert
        result.Should().Be(new DateTimeOffset(2026, 6, 10, 5, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void NextAllowedUtc_MidnightWrapAfterMidnight_DefersToTheCurrentLocalDayEnd()
    {
        // Arrange — 2026-06-10 01:00 CEST = 06-09 23:00Z: inside the same wrapped window, after
        // midnight, so the window ends on the CURRENT local day (06-10 07:00 CEST = 05:00Z).
        var nowUtc = new DateTimeOffset(2026, 6, 9, 23, 0, 0, TimeSpan.Zero);

        // Act
        var result = QuietHoursCalculator.NextAllowedUtc(nowUtc, QuietStart, QuietEnd, Prague);

        // Assert
        result.Should().Be(new DateTimeOffset(2026, 6, 10, 5, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void NextAllowedUtc_EqualBounds_IsAnEmptyWindow_ReturnsNowUnchanged()
    {
        // Arrange
        var nowUtc = new DateTimeOffset(2026, 6, 9, 20, 0, 0, TimeSpan.Zero);

        // Act
        var result = QuietHoursCalculator.NextAllowedUtc(
            nowUtc, new TimeOnly(22, 0), new TimeOnly(22, 0), Prague);

        // Assert
        result.Should().Be(nowUtc);
    }

    [Fact]
    public void NextAllowedUtc_SpringForward_InvalidEndTimeResolvesToTheSkippedForwardInstant()
    {
        // Arrange — Prague springs forward 2026-03-29: 02:00 CET jumps to 03:00 CEST, so a 02:30
        // window end does not exist that day. TimeZoneInfo's default (standard offset, +01:00) maps
        // it to 01:30Z — the instant the clock skipped to (03:30 CEST). Now: 01:00 CET = 00:00Z, inside.
        var nowUtc = new DateTimeOffset(2026, 3, 29, 0, 0, 0, TimeSpan.Zero);

        // Act
        var result = QuietHoursCalculator.NextAllowedUtc(
            nowUtc, new TimeOnly(22, 0), new TimeOnly(2, 30), Prague);

        // Assert
        result.Should().Be(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void NextAllowedUtc_FallBack_AmbiguousEndTimeResolvesToTheStandardTimeOccurrence()
    {
        // Arrange — Prague falls back 2026-10-25: 03:00 CEST returns to 02:00 CET, so 02:30 occurs
        // twice. TimeZoneInfo's default (standard offset, +01:00) picks the second occurrence =
        // 01:30Z. Now: 23:30 CEST on 10-24 = 21:30Z, inside the wrapped window.
        var nowUtc = new DateTimeOffset(2026, 10, 24, 21, 30, 0, TimeSpan.Zero);

        // Act
        var result = QuietHoursCalculator.NextAllowedUtc(
            nowUtc, new TimeOnly(22, 0), new TimeOnly(2, 30), Prague);

        // Assert
        result.Should().Be(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void NextAllowedUtc_FallBackRepeatedHour_ClassifiesTheTwoOccurrencesByInstantNotWallClock()
    {
        // Arrange — during the 2026-10-25 fall-back, wall-clock 02:45 happens twice in Prague:
        // first as CEST (00:45Z), then as CET (01:45Z). The window 22:00–02:30 ends at 01:30Z
        // (standard-offset resolution), so the FIRST occurrence is still quiet while the SECOND
        // is already past the end — a wall-clock comparison would classify both identically.
        var windowEndUtc = new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero);

        // Act + Assert — first occurrence (CEST) precedes the window's true end instant.
        var firstOccurrence = new DateTimeOffset(2026, 10, 25, 0, 45, 0, TimeSpan.Zero);
        QuietHoursCalculator.NextAllowedUtc(firstOccurrence, new TimeOnly(22, 0), new TimeOnly(2, 30), Prague)
            .Should().Be(windowEndUtc, "02:45 CEST precedes the window's true end instant");

        // Act + Assert — second occurrence (CET) is already past the window's true end instant.
        var secondOccurrence = new DateTimeOffset(2026, 10, 25, 1, 45, 0, TimeSpan.Zero);
        QuietHoursCalculator.NextAllowedUtc(secondOccurrence, new TimeOnly(22, 0), new TimeOnly(2, 30), Prague)
            .Should().Be(secondOccurrence, "02:45 CET is after the window's true end instant");
    }
}
