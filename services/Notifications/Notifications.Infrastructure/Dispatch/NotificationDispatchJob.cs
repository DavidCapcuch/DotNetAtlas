using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Dispatch;
using Notifications.Domain.Channels;
using Platform.SharedKernel.Exceptions;

namespace Notifications.Infrastructure.Dispatch;

/// <summary>
/// Hangfire job entry point for <b>durable</b> per-channel dispatch (<see cref="ChannelType.IsDurable"/>;
/// ephemeral channels ride the minimal-retry <see cref="EphemeralNotificationDispatchJob"/> instead).
/// Hangfire activates this concrete type within a job scope; it then resolves the channel's
/// <see cref="IChannelDispatcher"/> from Keyed DI by <see cref="ChannelType"/>. One enqueue per
/// resolved channel keeps channels isolated (independent retry and latency). See ADR-0032.
/// </summary>
/// <remarks>
/// The <c>[AutomaticRetry]</c> policy mirrors the <c>src/Weather</c> jobs (and overrides Hangfire's global
/// default of 10) but adds <c>ExceptOn = CriticalException</c>: every non-bug-class failure — a transient
/// SMTP fault (<see cref="EmailDispatchFailedException"/>, a <see cref="RetryableException"/>), a transient
/// DB fault, etc. — retries up to 3× with backoff, while a bug-class <see cref="DataIntegrityException"/>
/// (unknown template, missing subject/tokens, missing recipient preference) cannot self-heal and so
/// <b>fails fast</b> — parked Failed on the first attempt instead of burning all retries against a
/// deterministically-failing condition. This brings the Hangfire path in line with the Kafka consumer's
/// bug-class-DLT vs transient-retry split (ADR-0025/0032).
/// </remarks>
[AutomaticRetry(
    Attempts = 3,
    OnAttemptsExceeded = AttemptsExceededAction.Fail,
    LogEvents = true,
    ExceptOn = new[] { typeof(CriticalException) })]
public sealed class NotificationDispatchJob
{
    private readonly IServiceProvider _serviceProvider;

    public NotificationDispatchJob(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task ExecuteAsync(string channelName, NotificationDispatch dispatch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        var channel = ChannelType.FromName(channelName);
        var dispatcher = _serviceProvider.GetRequiredKeyedService<IChannelDispatcher>(channel);
        await dispatcher.DispatchAsync(dispatch, ct);
    }
}
