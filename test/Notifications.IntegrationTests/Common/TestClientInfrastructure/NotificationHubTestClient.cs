using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;
using Notifications.Application.Bell;
using TypedSignalR.Client;

namespace Notifications.FunctionalTests.Common.TestClientInfrastructure;

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

    public Task StartAsync() => _connection.StartAsync(_cancellationToken);

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

    public Task StopAsync() => _connection.StopAsync(_cancellationToken);

    public async Task ReceiveNotification(BellNotification notification)
    {
        await _received.Writer.WriteAsync(notification, _cancellationToken);
    }

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for one pushed notification; returns <c>null</c> if
    /// none arrives within the window.
    /// </summary>
    public async Task<BellNotification?> ConsumeOne(TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            if (await _received.Reader.WaitToReadAsync(cts.Token))
            {
                return await _received.Reader.ReadAsync(cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the timeout elapses with no message.
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        _subscription.Dispose();
        await _connection.DisposeAsync();
    }
}
