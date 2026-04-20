namespace Platform.SharedKernel.Time;

/// <summary>
/// Production <see cref="IClock"/> implementation backed by <see cref="DateTimeOffset.UtcNow"/>.
/// Registered as a singleton by <c>AddSharedKernel</c>.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc/>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
