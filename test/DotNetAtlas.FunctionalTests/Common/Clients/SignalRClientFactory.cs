using DotNetAtlas.Api.SignalRHubs.WeatherAlerts;
using DotNetAtlas.Application.Common.Observability;
using MessagePack;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAtlas.FunctionalTests.Common.Clients;

public sealed class SignalRClientFactory
{
    private readonly TestServer _server;
    private readonly string? _traceParent;
    private readonly IServiceScope _scope;
    private readonly CancellationToken _cancellationToken;

    public SignalRClientFactory(
        TestServer server,
        string? traceParent,
        IServiceScope scope,
        CancellationToken cancellationToken)
    {
        _server = server;
        _traceParent = traceParent;
        _scope = scope;
        _cancellationToken = cancellationToken;
    }

    public async Task<WeatherAlertHubTestClient> CreateAsync(ClientType clientType)
    {
        var accessToken = FakeTokenCreator.CreateUserToken(clientType);
        var hubUrl = new Uri("ws://localhost" + WeatherAlertHub.RoutePattern);

        var hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl.ToString(), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(
                    string.IsNullOrEmpty(accessToken) ? null : accessToken);
                options.Headers.Add("traceparent", _traceParent ?? "");
                options.Transports = HttpTransportType.WebSockets;
                options.HttpMessageHandlerFactory = _ => _server.CreateHandler();
                options.SkipNegotiation = false;
                options.WebSocketFactory = async (context, cancellationToken) =>
                {
                    var wsClient = _server.CreateWebSocketClient();
                    wsClient.SubProtocols.Add("messagepack");
                    wsClient.ConfigureRequest = req =>
                    {
                        if (!string.IsNullOrEmpty(accessToken))
                        {
                            req.Headers.Authorization = $"Bearer {accessToken}";
                        }

                        if (!string.IsNullOrEmpty(_traceParent))
                        {
                            req.Headers.TraceParent = _traceParent;
                        }
                    };
                    return await wsClient.ConnectAsync(context.Uri, cancellationToken);
                };
            })
            .WithAutomaticReconnect()
            .AddMessagePackProtocol(options => options.SerializerOptions = MessagePackSerializerOptions.Standard)
            .Build();

        var client = new WeatherAlertHubTestClient(
            hubConnection,
            _scope.ServiceProvider.GetRequiredService<IDotNetAtlasInstrumentation>(),
            _cancellationToken);

        await client.StartAsync();
        return client;
    }
}
