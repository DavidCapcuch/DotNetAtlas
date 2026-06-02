using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.KafkaFlow.DeadLetter;
using Platform.SharedKernel.Exceptions;

namespace Platform.KafkaFlow.DeadLetter.UnitTests;

/// <summary>
/// Truth table for <see cref="ConsumerRetry.IsRetryable"/> — the single classification source for
/// consumer retry-vs-dead-letter routing (ADR-0025). The predicate delegates the transient/poison
/// split to Npgsql's <c>DbException.IsTransient</c>, so these cases also pin Npgsql's behaviour for
/// the SQLSTATE classes the policy depends on.
/// </summary>
public class ConsumerRetryTests
{
    [Theory]
    [InlineData("08006")] // connection_failure
    [InlineData("40001")] // serialization_failure
    [InlineData("40P01")] // deadlock_detected
    [InlineData("53300")] // too_many_connections (53* insufficient_resources)
    [InlineData("57P03")] // cannot_connect_now (57P0*)
    [InlineData("58030")] // io_error (58*)
    public void IsRetryable_TransientPostgresSqlState_ReturnsTrue(string sqlState)
    {
        ConsumerRetry.IsRetryable(NewPostgresException(sqlState)).Should().BeTrue();
    }

    [Theory]
    [InlineData("23505")] // unique_violation
    [InlineData("23514")] // check_violation
    [InlineData("22001")] // string_data_right_truncation
    [InlineData("42703")] // undefined_column
    public void IsRetryable_PoisonPostgresSqlState_ReturnsFalse(string sqlState)
    {
        ConsumerRetry.IsRetryable(NewPostgresException(sqlState)).Should().BeFalse();
    }

    [Fact]
    public void IsRetryable_BareDbUpdateException_ReturnsFalse()
    {
        // A synthesized DbUpdateException with no inner DbException carries no transient signal -> poison.
        ConsumerRetry.IsRetryable(new DbUpdateException("no inner cause")).Should().BeFalse();
    }

    [Fact]
    public void IsRetryable_DbUpdateExceptionWrappingTransientPostgres_ReturnsTrue()
    {
        var exception = new DbUpdateException("save failed", NewPostgresException("40001"));
        ConsumerRetry.IsRetryable(exception).Should().BeTrue();
    }

    [Fact]
    public void IsRetryable_DbUpdateExceptionWrappingPoisonPostgres_ReturnsFalse()
    {
        var exception = new DbUpdateException("constraint violated", NewPostgresException("23505"));
        ConsumerRetry.IsRetryable(exception).Should().BeFalse();
    }

    [Fact]
    public void IsRetryable_RetryableException_ReturnsTrue()
    {
        ConsumerRetry.IsRetryable(new RetryableException("retry me")).Should().BeTrue();
    }

    [Fact]
    public void IsRetryable_DerivedRetryableException_ReturnsTrue()
    {
        // Mirrors Inventory's ReservationReleaseFailedException : RetryableException (Slice 4).
        ConsumerRetry.IsRetryable(new DerivedRetryable()).Should().BeTrue();
    }

    [Fact]
    public void IsRetryable_RetryableExceptionAsInner_ReturnsTrue()
    {
        var exception = new InvalidOperationException("wrapper", new RetryableException("inner retry"));
        ConsumerRetry.IsRetryable(exception).Should().BeTrue();
    }

    [Fact]
    public void IsRetryable_TimeoutException_ReturnsTrue()
    {
        ConsumerRetry.IsRetryable(new TimeoutException()).Should().BeTrue();
    }

    [Fact]
    public void IsRetryable_OperationCanceledException_ReturnsFalse()
    {
        // Graceful-shutdown signal: DeadLetterMiddleware rethrows it to keep the offset uncommitted.
        ConsumerRetry.IsRetryable(new OperationCanceledException()).Should().BeFalse();
    }

    [Fact]
    public void IsRetryable_TaskCanceledException_ReturnsFalse()
    {
        ConsumerRetry.IsRetryable(new TaskCanceledException()).Should().BeFalse();
    }

    [Fact]
    public void IsRetryable_PlainException_ReturnsFalse()
    {
        ConsumerRetry.IsRetryable(new InvalidOperationException("a bug")).Should().BeFalse();
    }

    [Fact]
    public void IsRetryable_ArgumentException_ReturnsFalse()
    {
        ConsumerRetry.IsRetryable(new ArgumentException("bad payload")).Should().BeFalse();
    }

    [Fact]
    public void IsRetryable_Null_ReturnsFalse()
    {
        ConsumerRetry.IsRetryable(null).Should().BeFalse();
    }

    private static PostgresException NewPostgresException(string sqlState) =>
        new(messageText: "test", severity: "ERROR", invariantSeverity: "ERROR", sqlState: sqlState);

    private sealed class DerivedRetryable : RetryableException;
}
