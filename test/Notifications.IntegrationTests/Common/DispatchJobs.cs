using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Dispatch;
using Notifications.Domain.Channels;
using Notifications.Infrastructure.Dispatch;
using Platform.SharedKernel.Exceptions;

namespace Notifications.IntegrationTests.Common;

/// <summary>
/// Runs a channel's dispatch through the Hangfire job class Hangfire itself activates — the
/// channel's outer entrance — so every call also exercises the keyed-DI registration and the
/// <see cref="ChannelType"/> name round-trip the job performs before reaching the dispatcher.
/// </summary>
internal static class DispatchJobs
{
    /// <summary>
    /// Picks the job class the way <c>HangfireChannelDispatchEnqueuer</c> does. Choosing the wrong
    /// one would be unobservable here while both share one body; they differ only in
    /// <c>[AutomaticRetry]</c>, which no in-process run evaluates.
    /// </summary>
    public static Task RunAsync(
        IServiceProvider services, ChannelType channel, NotificationDispatch dispatch, CancellationToken ct) =>
        channel.IsDurable
            ? services.GetRequiredService<NotificationDispatchJob>().ExecuteAsync(channel.Name, dispatch, ct)
            : services.GetRequiredService<EphemeralNotificationDispatchJob>().ExecuteAsync(channel.Name, dispatch, ct);

    public static async Task RunDispatchJobAsync(
        this IntegrationTestFixture fixture, ChannelType channel, NotificationDispatch dispatch, CancellationToken ct)
    {
        await using var scope = fixture.CreateScope();
        await RunAsync(scope.ServiceProvider, channel, dispatch, ct);
    }

    /// <summary>
    /// Asserts the job fails on the bug-class guard identified by <paramref name="expectedErrorCode"/>.
    /// Keyed on the stable error code rather than message text, so a case cannot pass on an earlier
    /// guard than the one it names.
    /// </summary>
    public static async Task<DataIntegrityException> AssertDispatchJobFailsAsync(
        this IntegrationTestFixture fixture,
        ChannelType channel,
        NotificationDispatch dispatch,
        string expectedErrorCode,
        CancellationToken ct)
    {
        var exception = await Assert.ThrowsAsync<DataIntegrityException>(
            () => fixture.RunDispatchJobAsync(channel, dispatch, ct));

        exception.ErrorCode.Should().Be(
            expectedErrorCode, "the case must fail on the guard it names, not on an earlier one");

        return exception;
    }
}
