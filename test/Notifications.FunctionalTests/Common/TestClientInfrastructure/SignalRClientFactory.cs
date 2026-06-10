using MessagePack;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Api.SignalRHubs;

namespace Notifications.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Builds <see cref="NotificationHubTestClient"/>s connected to the in-process bell hub over the
/// <see cref="TestServer"/>'s WebSocket transport (MessagePack protocol), carrying a bearer token
/// minted by <see cref="FakeTokenCreator"/>. Mirrors Weather's SignalR test-client factory,
/// re-targeted to <see cref="NotificationHub"/>.
/// </summary>
public sealed class SignalRClientFactory
{
    private readonly TestServer _server;
    private readonly string? _traceParent;
    private readonly FakeTokenCreator _tokenCreator;
    private readonly CancellationToken _cancellationToken;

    public SignalRClientFactory(
        TestServer server,
        string? traceParent,
        FakeTokenCreator tokenCreator,
        CancellationToken cancellationToken)
    {
        _server = server;
        _traceParent = traceParent;
        _tokenCreator = tokenCreator;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Connects an authenticated client for the given recipient (its <c>sub</c> = <paramref name="userId"/>).</summary>
    public Task<NotificationHubTestClient> ConnectAsAsync(Guid userId)
        => CreateAsync(_tokenCreator.CreateUserToken(ClientType.RegularUser, userId));

    /// <summary>Connects an unauthenticated client (no bearer token) — used to assert the hub rejects it.</summary>
    public Task<NotificationHubTestClient> ConnectUnauthenticatedAsync()
        => CreateAsync(_tokenCreator.CreateUserToken(ClientType.NonAuth));

    private async Task<NotificationHubTestClient> CreateAsync(string accessToken)
    {
        var hubUrl = new Uri("ws://localhost" + NotificationHub.RoutePattern);

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
            .AddMessagePackProtocol(options => options.SerializerOptions = MessagePackSerializerOptions.Standard)
            .Build();

        var client = new NotificationHubTestClient(hubConnection, _cancellationToken);
        await client.StartAsync();
        return client;
    }
}
