using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Dispatch;
using Notifications.Domain.Channels;

namespace Notifications.Infrastructure.Dispatch;

/// <summary>
/// Hangfire job entry point for per-channel dispatch. Hangfire activates this concrete type within a
/// job scope; it then resolves the channel's <see cref="IChannelDispatcher"/> from Keyed DI by
/// <see cref="ChannelType"/>. One enqueue per resolved channel keeps channels isolated (independent
/// retry and latency). The walking skeleton (#312) only ever enqueues the email channel. See ADR-0032.
/// </summary>
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
