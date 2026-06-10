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
/// The Hangfire retry policy on <see cref="EphemeralNotificationDispatchJob"/> (ADR-0032 § 3; the
/// <c>Attempts = 1</c> rationale lives on the job's own doc): the bug-class vs transient split is the
/// same as the durable job's — a <see cref="CriticalException"/> (e.g. the dispatcher's
/// <see cref="DataIntegrityException"/>s) is parked Failed on the first attempt via <c>ExceptOn</c>,
/// while a transient <see cref="RetryableException"/> gets exactly one retry. Exercises the job's
/// <i>actual</i> configured attribute against Hangfire's state machine, so the policy is proven at
/// runtime, not assumed.
/// </summary>
public sealed class EphemeralNotificationDispatchJobRetryPolicyTests
{
    private static readonly AutomaticRetryAttribute RetryPolicy =
        typeof(EphemeralNotificationDispatchJob)
            .GetCustomAttributes(typeof(AutomaticRetryAttribute), inherit: false)
            .Cast<AutomaticRetryAttribute>()
            .Single();

    [Fact]
    public void BugClassFailure_IsParkedFailed_OnTheFirstAttempt_WithoutRetry()
    {
        // Arrange — a DataIntegrityException (a CriticalException subclass) on a fresh job (RetryCount = 0).
        var context = BuildElectStateContext(
            new DataIntegrityException("Notifications.MissingBellTemplateChannel", "bug"), retryCount: 0);

        // Act
        RetryPolicy.OnStateElection(context);

        // Assert — the candidate stays Failed: ExceptOn excludes it from retry, so no reschedule.
        context.CandidateState.Should().BeOfType<FailedState>();
    }

    [Fact]
    public void TransientFailure_IsRescheduledForItsSingleRetry()
    {
        // Arrange — a RetryableException (transient, NOT bug-class) on a fresh job.
        var context = BuildElectStateContext(new RetryableException("transient"), retryCount: 0);

        // Act
        RetryPolicy.OnStateElection(context);

        // Assert — rescheduled for the one retry the ephemeral policy allows.
        context.CandidateState.Should().BeOfType<ScheduledState>();
    }

    [Fact]
    public void TransientFailure_AfterTheSingleRetry_IsParkedFailed()
    {
        // Arrange — the same transient failure, but the single retry has already been spent.
        var context = BuildElectStateContext(new RetryableException("still transient"), retryCount: 1);

        // Act
        RetryPolicy.OnStateElection(context);

        // Assert — Attempts = 1 is exhausted; the job parks Failed instead of rescheduling again.
        context.CandidateState.Should().BeOfType<FailedState>();
    }

    [Fact]
    public void EffectiveRetryPolicy_IsTheClassLevelAttribute_OverridingHangfireGlobalDefault()
    {
        // Resolve the filters Hangfire would actually apply to the job — global default + class-level,
        // deduped. Because AutomaticRetry.AllowMultiple is false, the class-level policy must REPLACE
        // Hangfire's global default (Attempts = 10), so exactly one remains with our configuration.
        var job = Job.FromExpression<EphemeralNotificationDispatchJob>(j => j.ExecuteAsync(default!, default!, default));
        var effectiveRetryPolicies = JobFilterProviders.Providers
            .GetFilters(job)
            .Select(filter => filter.Instance)
            .OfType<AutomaticRetryAttribute>()
            .ToList();

        using var _ = new AssertionScope();
        effectiveRetryPolicies.Should().ContainSingle("the class-level policy overrides Hangfire's global default(10)");
        effectiveRetryPolicies[0].Attempts.Should().Be(1);
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
            new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc));

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
