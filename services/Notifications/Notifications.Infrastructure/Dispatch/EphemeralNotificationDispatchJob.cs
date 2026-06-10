using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Dispatch;
using Notifications.Domain.Channels;
using Platform.SharedKernel.Exceptions;

namespace Notifications.Infrastructure.Dispatch;

/// <summary>
/// Hangfire job entry point for <b>ephemeral</b> per-channel dispatch (<see cref="ChannelType.IsDurable"/>
/// <c>== false</c> — the bell, #317). Identical resolution body to <see cref="NotificationDispatchJob"/>
/// (Keyed-DI <see cref="IChannelDispatcher"/> by <see cref="ChannelType"/>), but with the minimal retry
/// policy ADR-0032 § 3 prescribes for best-effort channels: <c>Attempts = 1</c> — a bell's value decays
/// with time, an offline recipient misses it by design, and there is no ledger to reconcile a late
/// success into, so one retry absorbs a transient blip and anything beyond that is waste. Kept as a
/// separate class — not derived from the durable job, since
/// <see cref="Hangfire.Common.JobFilterAttribute"/> is inherited and deriving would stack two
/// type-scoped retry filters — so the two policies stay independently visible (and the dashboard shows
/// ephemeral jobs under their own type name).
/// </summary>
[AutomaticRetry(
    Attempts = 1,
    OnAttemptsExceeded = AttemptsExceededAction.Fail,
    LogEvents = true,
    ExceptOn = new[] { typeof(CriticalException) })]
public sealed class EphemeralNotificationDispatchJob
{
    private readonly IServiceProvider _serviceProvider;

    public EphemeralNotificationDispatchJob(IServiceProvider serviceProvider)
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
