using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;
using Notifications.Application.Bell;
using TypedSignalR.Client;

namespace Notifications.IntegrationTests.Common.TestClientInfrastructure;

/// <summary>
/// In-test bell client: registers itself as the hub's <see cref="INotificationClientContract"/>
/// receiver and drains every server-pushed <see cref="BellNotification"/> into an unbounded channel
/// the test can await. The bell has no client-to-server RPC, so there is no hub proxy.
/// </summary>
public sealed class NotificationHubTestClient : INotificationClientContract, IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly Channel<BellNotification> _received;
    private readonly IDisposable _subscription;
    private readonly CancellationToken _cancellationToken;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public NotificationHubTestClient(HubConnection connection, CancellationToken cancellationToken)
    {
        _connection = connection;
        _cancellationToken = cancellationToken;
        _received = Channel.CreateUnbounded<BellNotification>();
        _subscription = _connection.Register<INotificationClientContract>(this);

        // Wired before StartAsync (the factory constructs this client first), so a server-side drop
        // during/after connect — e.g. the hub aborting a connection it cannot key to a recipient — is
        // observed rather than missed.
        _connection.Closed += _ =>
        {
            _closed.TrySetResult();
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Connects within <paramref name="timeout"/>. SignalR's handshake timeout only starts once the
    /// transport is up, so without this bound a stalled WebSocket upgrade would wait on the test token,
    /// which never fires on its own.
    /// </summary>
    public async Task StartAsync(TimeSpan timeout)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
        cts.CancelAfter(timeout);
        await _connection.StartAsync(cts.Token);
    }

    /// <summary>
    /// Completes <c>true</c> if the connection is (or becomes) closed within <paramref name="timeout"/>.
    /// Used to assert the hub drops a connection post-handshake (an authenticated token the hub cannot
    /// resolve to a recipient).
    /// </summary>
    public async Task<bool> WaitUntilClosedAsync(TimeSpan timeout)
    {
        if (_connection.State == HubConnectionState.Disconnected)
        {
            return true;
        }

        var finished = await Task.WhenAny(_closed.Task, Task.Delay(timeout, _cancellationToken));
        return finished == _closed.Task || _connection.State == HubConnectionState.Disconnected;
    }

    public async Task ReceiveNotification(BellNotification notification)
    {
        await _received.Writer.WriteAsync(notification, _cancellationToken);
    }

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for one pushed notification; returns <c>null</c> if
    /// none arrives within the window. Cancelling <paramref name="ct"/> propagates rather than
    /// reading as "nothing arrived".
    /// </summary>
    public async Task<BellNotification?> ConsumeOne(TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            // TryRead, not ReadAsync(token): ReadAsync checks cancellation before it dequeues, so a
            // window closing between the two awaits would strand an arrived message for the next read.
            while (await _received.Reader.WaitToReadAsync(cts.Token))
            {
                if (_received.Reader.TryRead(out var notification))
                {
                    return notification;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The window elapsed with nothing received.
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        _subscription.Dispose();
        await _connection.DisposeAsync();
    }
}
