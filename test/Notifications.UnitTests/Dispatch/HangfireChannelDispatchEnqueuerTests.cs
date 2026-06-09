using AwesomeAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Time.Testing;
using Notifications.Application.Dispatch;
using Notifications.Domain.Channels;
using Notifications.Infrastructure.Dispatch;
using NSubstitute;
using Xunit;

namespace Notifications.UnitTests.Dispatch;

/// <summary>
/// The split-time seam of ADR-0032 § 3: a future <c>executeAt</c> becomes a Hangfire
/// <see cref="ScheduledState"/> job (quiet-hours deferral), an immediate one stays a plain
/// fire-and-forget <see cref="EnqueuedState"/> enqueue (no schedule-poll latency). Hangfire's
/// <c>Enqueue</c>/<c>Schedule</c> are extension methods, so the assertions target the underlying
/// <c>Create(Job, IState)</c> they both forward to.
/// </summary>
public sealed class HangfireChannelDispatchEnqueuerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 9, 21, 30, 0, TimeSpan.Zero);

    private readonly IBackgroundJobClientV2 _backgroundJobs = Substitute.For<IBackgroundJobClientV2>();
    private readonly HangfireChannelDispatchEnqueuer _enqueuer;

    public HangfireChannelDispatchEnqueuerTests()
    {
        _enqueuer = new HangfireChannelDispatchEnqueuer(_backgroundJobs, new FakeTimeProvider(Now));
    }

    [Fact]
    public void Enqueue_ExecuteAtNow_CreatesAnImmediateEnqueuedJob()
    {
        // Act
        _enqueuer.Enqueue(ChannelType.Email, BuildDispatch(), executeAt: Now);

        // Assert
        _backgroundJobs.Received(1).Create(
            Arg.Is<Job>(job => job.Type == typeof(NotificationDispatchJob) && Equals(job.Args[0], "Email")),
            Arg.Any<EnqueuedState>());
    }

    [Fact]
    public void Enqueue_FutureExecuteAt_CreatesAScheduledJobForThatInstant()
    {
        // Arrange
        var executeAt = Now.AddHours(7.5);

        // Act
        _enqueuer.Enqueue(ChannelType.Sms, BuildDispatch(), executeAt);

        // Assert
        _backgroundJobs.Received(1).Create(
            Arg.Is<Job>(job => job.Type == typeof(NotificationDispatchJob) && Equals(job.Args[0], "Sms")),
            Arg.Is<ScheduledState>(state => state.EnqueueAt == executeAt.UtcDateTime));
    }

    [Fact]
    public void Enqueue_PastExecuteAt_CreatesAnImmediateEnqueuedJob()
    {
        // Act — a stale executeAt (e.g. the quiet window ended while the Kafka re-drive was in
        // flight) must dispatch immediately, not round-trip through the scheduler.
        _enqueuer.Enqueue(ChannelType.Sms, BuildDispatch(), executeAt: Now.AddMinutes(-5));

        // Assert
        _backgroundJobs.Received(1).Create(
            Arg.Is<Job>(job => job.Type == typeof(NotificationDispatchJob)),
            Arg.Any<EnqueuedState>());
    }

    private static NotificationDispatch BuildDispatch() => new()
    {
        NotificationId = Guid.CreateVersion7(),
        RecipientUserId = Guid.CreateVersion7(),
        TemplateKey = "order.shipped",
        Payload = [],
    };
}
