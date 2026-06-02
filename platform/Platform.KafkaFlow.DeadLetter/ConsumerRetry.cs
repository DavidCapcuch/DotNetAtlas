using System.Data.Common;
using Platform.SharedKernel.Exceptions;

namespace Platform.KafkaFlow.DeadLetter;

/// <summary>
/// Single source of truth for classifying a consumed-message failure as <em>retryable</em> (transient
/// infrastructure fault or a deliberately-retryable signal) versus <em>poison</em> (route to the
/// dead-letter topic). Every bounded context's consumer wiring references this from its
/// <c>RetryForever(c =&gt; c.Handle(ctx =&gt; ConsumerRetry.IsRetryable(ctx.Exception)))</c> call (ADR-0025).
/// </summary>
public static class ConsumerRetry
{
    /// <summary>
    /// Returns <see langword="true"/> when the failure should be retried (with the consumer paused)
    /// rather than dead-lettered.
    /// </summary>
    /// <remarks>
    /// Retryable iff, anywhere in the exception's inner-exception chain, there is a
    /// <see cref="RetryableException"/>, a <see cref="TimeoutException"/>, or a <see cref="DbException"/>
    /// whose <see cref="DbException.IsTransient"/> is <see langword="true"/> (Npgsql flags the transient
    /// SQLSTATE classes — <c>08*</c>, <c>40001</c>, <c>40P01</c>, <c>53*</c>, <c>57P0*</c>, <c>58*</c>).
    /// Everything else is poison: integrity / data / syntax violations (<c>23*</c>/<c>22*</c>/<c>42*</c>),
    /// a bare synthesized <c>DbUpdateException</c> with no inner <see cref="DbException"/>, deserialization
    /// failures, and any unrecognised exception. <see cref="OperationCanceledException"/> (including
    /// <see cref="TaskCanceledException"/>) is never retryable — it is the graceful-shutdown signal that
    /// <see cref="DeadLetterMiddleware"/> rethrows to keep the offset uncommitted.
    /// </remarks>
    /// <param name="exception">The exception thrown while processing the consumed message.</param>
    /// <returns><see langword="true"/> if the message should be retried; otherwise <see langword="false"/>.</returns>
    public static bool IsRetryable(Exception? exception)
    {
        // Graceful-shutdown cancellation must never be swallowed into a retry/dead-letter decision.
        if (exception is OperationCanceledException)
        {
            return false;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is RetryableException or TimeoutException or DbException { IsTransient: true })
            {
                return true;
            }
        }

        return false;
    }
}
