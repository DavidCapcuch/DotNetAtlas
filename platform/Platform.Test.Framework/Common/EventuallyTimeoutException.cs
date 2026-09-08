namespace Platform.Test.Framework.Common;

/// <summary>
/// Thrown when <see cref="Eventually"/> gives up on a condition that never held.
/// </summary>
/// <remarks>
/// Distinct from a bare <see cref="TimeoutException"/> so a caller can catch this helper's own
/// deadline without also catching an infrastructure timeout. Several clients derive theirs from
/// <see cref="TimeoutException"/> — <c>RedisTimeoutException</c> among them — and an undiscriminating
/// catch would report a wedged datastore as a condition that never held. No probe surfaces one today,
/// which is why the distinction is worth holding while it is still free. Where the last probe faulted
/// rather than answered, that fault is the inner exception.
/// </remarks>
public sealed class EventuallyTimeoutException(string message, Exception? innerException = null)
    : TimeoutException(message, innerException);
