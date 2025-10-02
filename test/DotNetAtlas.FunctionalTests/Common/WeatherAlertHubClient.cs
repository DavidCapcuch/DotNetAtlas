using System.Threading.Channels;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using TypedSignalR.Client;

namespace DotNetAtlas.FunctionalTests.Common;

public class WeatherAlertHubClient : IWeatherAlertClientContract, IAsyncDisposable
{
    protected internal HubConnection Connection { get; }
    protected internal Channel<WeatherAlertMessage> ReceivedMessages { get; }
    private readonly IWeatherAlertHubContract _server;
    private readonly IDotNetAtlasInstrumentation _dotNetAtlasInstrumentation;
    private readonly IDisposable _subscription;
    private readonly CancellationToken _cancellationToken;

    public WeatherAlertHubClient(
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

    public async Task<List<WeatherAlertMessage>> GetAllReceivedMessagesAsync()
    {
        var weatherAlertMessages = new List<WeatherAlertMessage>();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));
        try
        {
            while (await ReceivedMessages.Reader.WaitToReadAsync(cts.Token))
            {
                weatherAlertMessages.Add(await ReceivedMessages.Reader.ReadAsync(cts.Token));
            }
        }
        catch (OperationCanceledException) { }

        return weatherAlertMessages;
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
