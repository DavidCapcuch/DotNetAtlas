namespace Platform.SharedKernel.Time;

/// <summary>
/// Abstraction over the ambient wall clock (ADR-0015).
/// Domain and application code depend on <see cref="IClock"/> instead of
/// <see cref="DateTimeOffset.UtcNow"/> directly so tests can freeze or advance time.
/// </summary>
public interface IClock
{
    /// <summary>
    /// Current UTC instant as a <see cref="DateTimeOffset"/>.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}
