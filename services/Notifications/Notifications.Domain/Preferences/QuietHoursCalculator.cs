namespace Notifications.Domain.Preferences;

/// <summary>
/// The pure quiet-hours scheduling rule (notifications.md § 5.4): when a quiet-hours-respecting
/// channel would fire inside the recipient's quiet window, defer it to the window's end. See ADR-0015
/// for the <see cref="TimeZoneInfo"/>-over-NodaTime policy.
/// </summary>
public static class QuietHoursCalculator
{
    /// <summary>
    /// Returns the earliest UTC instant at or after <paramref name="nowUtc"/> at which a
    /// quiet-hours-respecting channel may dispatch: <paramref name="nowUtc"/> itself when the
    /// recipient has no quiet hours (<c>null</c> bounds) or is outside the window, else the
    /// configured <paramref name="quietEnd"/> resolved against the local date on which the window
    /// ends (the next local day when the window wraps past midnight), converted local→UTC.
    /// </summary>
    /// <remarks>
    /// The in/out-of-window check compares <b>UTC instants</b> of the window's edges, never the
    /// recipient's wall-clock, so a DST fall-back repeated hour cannot classify one instant both
    /// ways. An ambiguous or invalid (skipped) local edge resolves via <see cref="TimeZoneInfo"/>'s
    /// defaults: the standard-time offset on fall-back, the skip-forward adjustment on spring-forward.
    /// Equal bounds are an empty <c>[start, end)</c> window (never quiet); the both-or-neither
    /// invariant on half-specified bounds is enforced upstream by <see cref="NotificationPreference"/>.
    /// </remarks>
    public static DateTimeOffset NextAllowedUtc(
        DateTimeOffset nowUtc,
        TimeOnly? quietStart,
        TimeOnly? quietEnd,
        string ianaTz)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ianaTz);

        if (quietStart is not { } start || quietEnd is not { } end || start == end)
        {
            return nowUtc;
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(ianaTz);
        var localDateOfNow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, timeZone).DateTime);

        // The daily window containing nowUtc can be anchored on the recipient's local yesterday
        // (a wrapped window straddling midnight), today, or — in the extreme case of a timezone
        // transition at the local date line — tomorrow. Probe all three anchors and compare on
        // UTC instants.
        for (var dayOffset = -1; dayOffset <= 1; dayOffset++)
        {
            var startDate = localDateOfNow.AddDays(dayOffset);
            var endDate = end > start ? startDate : startDate.AddDays(1);

            var windowStartUtc = ResolveLocalToUtc(startDate.ToDateTime(start), timeZone);
            var windowEndUtc = ResolveLocalToUtc(endDate.ToDateTime(end), timeZone);

            if (nowUtc >= windowStartUtc && nowUtc < windowEndUtc)
            {
                return windowEndUtc;
            }
        }

        return nowUtc;
    }

    /// <summary>
    /// Maps a civil wall-clock time to its UTC instant using <see cref="TimeZoneInfo.GetUtcOffset(DateTime)"/>,
    /// whose documented defaults give exactly the DST resolution § 5.4 specifies: an ambiguous time
    /// (fall-back) takes the standard-time offset, an invalid time (spring-forward gap) also takes the
    /// standard-time offset — which lands on the instant the clock skipped forward to.
    /// </summary>
    private static DateTimeOffset ResolveLocalToUtc(DateTime local, TimeZoneInfo timeZone)
    {
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local)).ToUniversalTime();
    }
}
