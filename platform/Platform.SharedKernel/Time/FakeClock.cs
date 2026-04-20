namespace Platform.SharedKernel.Time;

/// <summary>
/// Deterministic <see cref="IClock"/> for tests. Not thread-safe.
/// </summary>
public sealed class FakeClock : IClock
{
    private DateTimeOffset _now;

    /// <summary>
    /// Creates a fake clock pinned at <paramref name="initial"/>.
    /// </summary>
    /// <param name="initial">Initial instant.</param>
    public FakeClock(DateTimeOffset initial)
    {
        _now = initial;
    }

    /// <inheritdoc/>
    public DateTimeOffset UtcNow => _now;

    /// <summary>
    /// Jumps the clock forward by <paramref name="delta"/>.
    /// </summary>
    /// <param name="delta">Positive or negative <see cref="TimeSpan"/>.</param>
    public void Advance(TimeSpan delta) => _now = _now.Add(delta);

    /// <summary>
    /// Pins the clock at <paramref name="instant"/>.
    /// </summary>
    /// <param name="instant">Target instant.</param>
    public void Set(DateTimeOffset instant) => _now = instant;
}
