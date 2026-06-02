namespace Platform.SharedKernel.Exceptions;

/// <summary>
/// Marker exception a handler throws to deliberately request a consumer retry for a failure that is
/// recoverable but NOT surfaced as a transient <see cref="System.Data.Common.DbException"/> — e.g. a
/// business-transient condition, or an at-least-once redelivery the handler wants re-run.
/// </summary>
/// <remarks>
/// <para>
/// The Kafka consumer retry classifier (<c>Platform.KafkaFlow.DeadLetter.ConsumerRetry.IsRetryable</c>)
/// treats this type — and anything derived from it — as retryable, so the message is retried (the
/// consumer paused) instead of being dead-lettered. It is the only non-<c>IsTransient</c> way to request
/// a retry and the deliberate escape hatch for failures Npgsql does not flag transient (ADR-0025).
/// </para>
/// <para>
/// It lives in the shared kernel (not the messaging layer) so Application / Infrastructure handlers can
/// signal "retry me" without taking a KafkaFlow dependency. It is intentionally NOT a
/// <see cref="CriticalException"/>: a <see cref="CriticalException"/> means "this is a bug, dead-letter
/// it"; a <see cref="RetryableException"/> means the opposite.
/// </para>
/// </remarks>
public class RetryableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RetryableException"/> class.
    /// </summary>
    public RetryableException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryableException"/> class with a description.
    /// </summary>
    /// <param name="message">A description of the retryable condition.</param>
    public RetryableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryableException"/> class with a description and cause.
    /// </summary>
    /// <param name="message">A description of the retryable condition.</param>
    /// <param name="innerException">The underlying cause to retry on.</param>
    public RetryableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
