using System.Text.Json.Nodes;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Swagger;

namespace Platform.Api.UnitTests.Swagger.Probe;

/// <summary>
/// In-memory FastEndpoints host wired the way a bounded-context API wires itself, serving the probe
/// endpoints in this namespace. Tests read the contract by fetching the document over HTTP — the
/// seam ADR-0038 § Decision picks deliberately.
/// </summary>
internal sealed class ProbeApiHost : IAsyncDisposable
{
    /// <summary>Keycloak realm authority a developer or test tier supplies.</summary>
    internal const string Authority = "http://localhost:9011/realms/dotnetatlas";

    private const string DocumentRoute = "/swagger/v1/swagger.json";

    private readonly WebApplication _app;
    private readonly HttpClient _client;

    private ProbeApiHost(WebApplication app)
    {
        _app = app;
        _client = app.GetTestClient();
    }

    /// <summary>
    /// Starts the host. Pass <paramref name="authority"/> as <c>null</c> to model a tier that drops
    /// <c>Authentication:JwtBearer:Authority</c> — the document is then served without an OAuth2
    /// security scheme, which is the branch that returns early out of the document-settings callback.
    /// </summary>
    internal static async Task<ProbeApiHost> StartAsync(string? authority = Authority)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:JwtBearer:Authority"] = authority,
        });

        builder.Services.AddAuthorization();
        builder.Services.AddFastEndpoints(o =>
        {
            // Scoped to this namespace, not the whole assembly: any endpoint added anywhere in the
            // test project would otherwise leak its schemas into the document under assertion.
            o.Assemblies = [typeof(ContractProbeEndpoint).Assembly];
            o.Filter = endpointType => endpointType.Namespace?.StartsWith(
                typeof(ContractProbeEndpoint).Namespace!, StringComparison.Ordinal) == true;
        });
        builder.Services.AddPlatformAuthSwaggerDocument(
            builder.Configuration,
            "Contract Probe API",
            "v1",
            "Probe surface for the OpenAPI contract tests.");

        var app = builder.Build();
        app.UseFastEndpoints(config =>
        {
            config.Versioning.Prefix = "v";
            config.Versioning.PrependToRoute = true;
            config.Versioning.DefaultVersion = 1;
            config.Endpoints.RoutePrefix = "api";
        });
        app.UseSwaggerGen();

        await app.StartAsync(TestContext.Current.CancellationToken);

        return new ProbeApiHost(app);
    }

    internal async Task<string> GetOpenApiDocumentJsonAsync()
    {
        using var response = await _client.GetAsync(DocumentRoute, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    internal async Task<JsonNode> GetOpenApiDocumentAsync()
        => JsonNode.Parse(await GetOpenApiDocumentJsonAsync())!;

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }
}
