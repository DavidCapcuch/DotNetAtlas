using System.Threading.Channels;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using TypedSignalR.Client;

namespace DotNetAtlas.FunctionalTests.Common.Clients;

public class WeatherAlertHubTestClient : IWeatherAlertClientContract, IAsyncDisposable
{
    protected internal HubConnection Connection { get; }
    protected internal Channel<WeatherAlertMessage> ReceivedMessages { get; }
    private readonly IWeatherAlertHubContract _server;
    private readonly IDotNetAtlasInstrumentation _dotNetAtlasInstrumentation;
    private readonly IDisposable _subscription;
    private readonly CancellationToken _cancellationToken;

    public WeatherAlertHubTestClient(
        HubConnection connection,
        IDotNetAtlasInstrumentation dotNetAtlasInstrumentation,
        CancellationToken cancellationToken)
    {
        Connection = connection;
        _dotNetAtlasInstrumentation = dotNetAtlasInstrumentation;
        _cancellationToken = cancellationToken;
        ReceivedMessages = Channel.CreateUnbounded<WeatherAlertMessage>();
        _server = Connection.CreateHubProxy<IWeatherAlertHubContract>(_cancellationToken);
        _subscription = Connection.Register<IWeatherAlertClientContract>(this);
    }

    public async Task StartAsync()
    {
        await Connection.StartAsync(_cancellationToken);
    }

    public async Task StopAsync()
    {
        await Connection.StopAsync(_cancellationToken);
    }

    public async Task SubscribeForCityAlertsAsync(AlertSubscriptionDto subscription)
    {
        await Connection.InvokeAsync("SubscribeForCityAlerts", subscription, _cancellationToken);
    }

    public async Task UnsubscribeFromCityAlertsAsync(AlertSubscriptionDto subscription)
    {
        await _server.UnsubscribeFromCityAlerts(subscription);
    }

    public async Task SendWeatherAlertAsync(IAsyncEnumerable<WeatherAlert> alerts)
    {
        await _server.SendWeatherAlert(alerts);
    }

    /// <summary>
    /// Consumes one message from the SignalR hub within the timeout period.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for a message.</param>
    /// <param name="ct">Optional cancellation token to cancel the operation.</param>
    /// <returns>The received message, or null if no message was received within the timeout.</returns>
    public async Task<WeatherAlertMessage?> ConsumeOne(TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            if (await ReceivedMessages.Reader.WaitToReadAsync(cts.Token))
            {
                return await ReceivedMessages.Reader.ReadAsync(cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when timeout is reached
        }

        return null;
    }

    /// <summary>
    /// Consumes multiple messages from the SignalR hub within the specified timeout.
    /// Continues reading until the timeout expires or maxCount is reached.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for messages.</param>
    /// <param name="maxCount">Maximum number of messages to consume (default 10 for individual test runs).</param>
    /// <param name="ct">Optional cancellation token to cancel the operation.</param>
    /// <returns>List of all consumed messages.</returns>
    public async Task<List<WeatherAlertMessage>> ConsumeMultiple(
        TimeSpan timeout,
        int maxCount = 10,
        CancellationToken ct = default)
    {
        var messages = new List<WeatherAlertMessage>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        while (!cts.IsCancellationRequested && messages.Count < maxCount)
        {
            var message = await ConsumeOne(timeout, cts.Token);
            if (message != null)
            {
                messages.Add(message);
            }
        }

        return messages;
    }

    public async Task ReceiveWeatherAlert(WeatherAlertMessage weatherAlertMessage)
    {
        using var activity = _dotNetAtlasInstrumentation.StartActivity(nameof(ReceiveWeatherAlert));

        await ReceivedMessages.Writer.WriteAsync(weatherAlertMessage, _cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _subscription.Dispose();
        await Connection.DisposeAsync();
    }
}
