using AwesomeAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Notifications.Infrastructure.Dispatch;
using NSubstitute;
using Platform.SharedKernel.Exceptions;
using Xunit;

namespace Notifications.UnitTests.Dispatch;

/// <summary>
/// The Hangfire retry policy on <see cref="NotificationDispatchJob"/> (ADR-0032): bug-class failures
/// (the dispatcher's <see cref="DataIntegrityException"/>s — a <see cref="CriticalException"/>) cannot
/// self-heal, so the job's <c>[AutomaticRetry(ExceptOn = CriticalException)]</c> parks them Failed on the
/// first attempt; transient failures (<see cref="EmailDispatchFailedException"/>) still retry. Exercises
/// the job's <i>actual</i> configured attribute against Hangfire's state machine, so the <c>ExceptOn</c>
/// subclass-matching (base <see cref="CriticalException"/> excluding the <see cref="DataIntegrityException"/>
/// subclass) is proven at runtime, not assumed.
/// </summary>
public sealed class NotificationDispatchJobRetryPolicyTests
{
    private static readonly AutomaticRetryAttribute RetryPolicy =
        typeof(NotificationDispatchJob)
            .GetCustomAttributes(typeof(AutomaticRetryAttribute), inherit: false)
            .Cast<AutomaticRetryAttribute>()
            .Single();

    [Fact]
    public void BugClassFailure_IsParkedFailed_OnTheFirstAttempt_WithoutRetry()
    {
        // Arrange — a DataIntegrityException (a CriticalException subclass) on a fresh job (RetryCount = 0).
        var context = BuildElectStateContext(
            new DataIntegrityException("Notifications.MissingEmailTemplateChannel", "bug"), retryCount: 0);

        // Act
        RetryPolicy.OnStateElection(context);

        // Assert — the candidate stays Failed: ExceptOn excludes it from retry, so no reschedule.
        context.CandidateState.Should().BeOfType<FailedState>();
    }

    [Fact]
    public void TransientFailure_IsRescheduledForRetry()
    {
        // Arrange — an EmailDispatchFailedException (a RetryableException, NOT bug-class) on a fresh job.
        var context = BuildElectStateContext(
            new EmailDispatchFailedException(Guid.Empty, "smtp down"), retryCount: 0);

        // Act
        RetryPolicy.OnStateElection(context);

        // Assert — rescheduled for a later attempt.
        context.CandidateState.Should().BeOfType<ScheduledState>();
    }

    [Fact]
    public void EffectiveRetryPolicy_IsTheClassLevelAttribute_OverridingHangfireGlobalDefault()
    {
        // Resolve the filters Hangfire would actually apply to the job — global default + class-level,
        // deduped. Because AutomaticRetry.AllowMultiple is false, the class-level policy must REPLACE
        // Hangfire's global default (Attempts = 10), so exactly one remains with our configuration.
        var job = Job.FromExpression<NotificationDispatchJob>(j => j.ExecuteAsync(default!, default!, default));
        var effectiveRetryPolicies = JobFilterProviders.Providers
            .GetFilters(job)
            .Select(filter => filter.Instance)
            .OfType<AutomaticRetryAttribute>()
            .ToList();

        using var _ = new AssertionScope();
        effectiveRetryPolicies.Should().ContainSingle("the class-level policy overrides Hangfire's global default(10)");
        effectiveRetryPolicies[0].Attempts.Should().Be(3);
        effectiveRetryPolicies[0].OnAttemptsExceeded.Should().Be(AttemptsExceededAction.Fail);
        effectiveRetryPolicies[0].ExceptOn.Should().Contain(typeof(CriticalException));
    }

    private static ElectStateContext BuildElectStateContext(Exception failure, int retryCount)
    {
        var storage = Substitute.For<JobStorage>();
        var connection = Substitute.For<IStorageConnection>();
        var transaction = Substitute.For<IWriteOnlyTransaction>();
        connection.GetJobParameter("job-1", "RetryCount")
            .Returns(retryCount == 0 ? null : retryCount.ToString());

        var backgroundJob = new BackgroundJob(
            "job-1",
            Job.FromExpression<DispatchJobProbe>(probe => probe.Run()),
            new DateTime(2026, 6, 9, 0, 0, 0, DateTimeKind.Utc));

        var applyContext = new ApplyStateContext(
            storage, connection, transaction, backgroundJob, new FailedState(failure), oldStateName: "Processing");

        return new ElectStateContext(applyContext);
    }

    /// <summary>A throwaway public job target so Hangfire's <see cref="Job.FromExpression{T}"/> resolves a valid method.</summary>
    public sealed class DispatchJobProbe
    {
        public void Run()
        {
        }
    }
}
