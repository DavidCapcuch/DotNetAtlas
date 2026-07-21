using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Platform.ServiceDefaults.Auth;
using Platform.Test.Framework;

namespace EShop.BFF.IntegrationTests.Auth;

/// <summary>
/// The one isolated proof that the buyer-scoped token-exchange path works end-to-end against the
/// <b>pinned Keycloak 26.3.2</b> and the <b>real <c>realm-export.json</c></b> (issue #329 acceptance + the
/// HITL gate). The existing FunctionalTests mint a synthetic buyer-<c>sub</c> + callee-<c>aud</c> token that
/// no real Keycloak flow produces; this test closes that gap. It exercises the real
/// <see cref="TokenExchangeHandler"/> performing a Keycloak <b>Standard Token Exchange</b> (RFC 8693):
/// a user token (<c>aud: bff</c>) is exchanged on the <c>basket.read</c> scope into a token that
/// <b>Basket's own JwtBearer validator accepts</b> (<c>aud: basket-service</c>) while <b>preserving the
/// buyer <c>sub</c></b> — so Basket resolves the correct buyer. If the realm used the legacy token-exchange
/// model (or omitted <c>standard.token.exchange.enabled</c> / the <c>aud: bff</c> mapper), the exchange
/// would fail here.
/// </summary>
public sealed class TokenExchangeKeycloakTests : IAsyncLifetime
{
    private const string Realm = "dotnetatlas";
    private const string BffClientId = "bff";
    private const string BffClientSecret = "dev-bff-secret-rotate-in-prod";
    private const string SwaggerClientId = "dotnetatlas-swagger";
    private const string DevUsername = "dev@dotnetatlas.com";
    private const string DevPassword = "123456789";
    private const string BasketAudience = "basket-service";

    private readonly IContainer _keycloak = new ContainerBuilder("quay.io/keycloak/keycloak:26.3.2")
        .WithName($"TestKeycloakTokenExchange-{Guid.NewGuid()}")
        .WithResourceMapping(
            File.ReadAllBytes(Path.Combine(SolutionPaths.GetSolutionRootDirectory(), "src", "keycloak", "realm-export.json")),
            "/opt/keycloak/data/import/realm-export.json")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
        .WithCommand("start-dev", "--import-realm")
        .WithPortBinding(8080, true)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(r => r
                .ForPort(8080)
                .ForPath($"/realms/{Realm}/.well-known/openid-configuration")))
        .WithCleanUp(true)
        .Build();

    private HttpClient _http = null!;
    private string _baseUrl = null!;

    public async ValueTask InitializeAsync()
    {
        await _keycloak.StartAsync();
        _baseUrl = $"http://localhost:{_keycloak.GetMappedPublicPort(8080)}";
        _http = new HttpClient();
    }

    public async ValueTask DisposeAsync()
    {
        _http?.Dispose();
        await _keycloak.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task BffExchangesUserTokenForBasketReadToken_AcceptedByBasketAndPreservesBuyer()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — make the swagger (user-facing) client ROPC-able at runtime so the test can mint a real
        // user token without an auth-code browser flow. The committed realm keeps direct grants OFF.
        var adminToken = await GetAdminTokenAsync(ct);
        await EnableDirectAccessGrantsOnSwaggerAsync(adminToken, ct);

        // A real Keycloak-issued user token. The swagger client's audience-bff mapper stamps aud: bff — the
        // Standard-Token-Exchange v2 holder constraint (the requester must be in the subject token's aud).
        var userToken = await GetUserTokenViaRopcAsync(ct);
        var decodedUser = new JsonWebToken(userToken);
        var buyerSub = decodedUser.Subject;

        using (new AssertionScope())
        {
            decodedUser.Audiences.Should().Contain(BffClientId, "v2 only exchanges a subject token audienced for the requester (bff)");
            buyerSub.Should().NotBeNullOrEmpty();

            // Act — drive the REAL TokenExchangeHandler against the REAL Keycloak: exchange the user token on
            // the basket.read scope. (A failed exchange throws out of the handler and fails the test.)
            var exchangedToken = await ExchangeViaHandlerAsync(userToken, buyerSub, scope: "basket.read", ct);

            // Assert (1): the exchanged token is re-audienced to Basket and PRESERVES the buyer sub.
            var decodedExchanged = new JsonWebToken(exchangedToken);
            decodedExchanged.Audiences.Should().Contain(BasketAudience, "the basket.read scope's audience mapper re-audiences to basket-service");
            decodedExchanged.Subject.Should().Be(buyerSub, "token exchange preserves the buyer sub — Basket derives the owner from it");

            // Assert (2): Basket's OWN JwtBearer validator accepts the exchanged token (the proof that it would
            // authenticate at the real Basket service and resolve this buyer).
            var validation = await ValidateAsBasketWouldAsync(exchangedToken, ct);
            validation.IsValid.Should().BeTrue("Basket's JwtBearer (ValidAudience=basket-service, signed by the realm) must accept the exchanged token");
            validation.Claims["sub"].ToString().Should().Be(buyerSub, "Basket resolves the correct buyer from the validated sub");
        }
    }

    private async Task<string> GetAdminTokenAsync(CancellationToken ct) =>
        await PostTokenFormAsync("master", new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = "admin",
            ["password"] = "admin",
        }, ct);

    private async Task EnableDirectAccessGrantsOnSwaggerAsync(string adminToken, CancellationToken ct)
    {
        using var listReq = new HttpRequestMessage(
            HttpMethod.Get, $"{_baseUrl}/admin/realms/{Realm}/clients?clientId={SwaggerClientId}");
        listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var listResp = await _http.SendAsync(listReq, ct);
        listResp.EnsureSuccessStatusCode();
        var clients = JsonNode.Parse(await listResp.Content.ReadAsStringAsync(ct))!.AsArray();
        var clientUuid = clients[0]!["id"]!.GetValue<string>();

        using var getReq = new HttpRequestMessage(
            HttpMethod.Get, $"{_baseUrl}/admin/realms/{Realm}/clients/{clientUuid}");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var getResp = await _http.SendAsync(getReq, ct);
        getResp.EnsureSuccessStatusCode();
        var rep = JsonNode.Parse(await getResp.Content.ReadAsStringAsync(ct))!;
        rep["directAccessGrantsEnabled"] = true;

        using var putReq = new HttpRequestMessage(
            HttpMethod.Put, $"{_baseUrl}/admin/realms/{Realm}/clients/{clientUuid}")
        {
            Content = new StringContent(rep.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var putResp = await _http.SendAsync(putReq, ct);
        putResp.EnsureSuccessStatusCode();
    }

    private Task<string> GetUserTokenViaRopcAsync(CancellationToken ct) =>
        PostTokenFormAsync(Realm, new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = SwaggerClientId,
            ["username"] = DevUsername,
            ["password"] = DevPassword,
            ["scope"] = "openid",
        }, ct);

    private async Task<string> ExchangeViaHandlerAsync(string userToken, string buyerSub, string scope, CancellationToken ct)
    {
        var options = new ServiceAuthOptions
        {
            Authority = $"{_baseUrl}/realms/{Realm}",
            ClientId = BffClientId,
            ClientSecret = BffClientSecret,
            ServiceName = BffClientId,
        };

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {userToken}";
        var identity = new ClaimsIdentity(authenticationType: "test");
        identity.AddClaim(new Claim("sub", buyerSub));
        context.User = new ClaimsPrincipal(identity);

        var handler = new TokenExchangeHandler(
            new StaticOptionsMonitor(options),
            new PlainHttpClientFactory(),
            new HttpContextAccessor { HttpContext = context },
            TimeProvider.System,
            NullLogger<TokenExchangeHandler>.Instance);

        var capture = new CapturingHandler();
        handler.InnerHandler = capture;

        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("http://basket.invalid/api/v1/basket"));
        request.Options.Set(TokenExchangeHandler.ScopeRequestOptionKey, scope);
        (await client.SendAsync(request, ct)).Dispose();

        capture.CapturedBearer.Should().NotBeNullOrEmpty("the handler must attach the exchanged bearer token");
        return capture.CapturedBearer!;
    }

    private async Task<TokenValidationResult> ValidateAsBasketWouldAsync(string exchangedToken, CancellationToken ct)
    {
        var jwks = await _http.GetStringAsync(
            $"{_baseUrl}/realms/{Realm}/protocol/openid-connect/certs", ct);

        // Mirrors Basket's effective JwtBearer validation (services/Basket appsettings +
        // JwtBearerConfigurator): ValidAudience=basket-service, all five flags true, iss checked against
        // the realm. Basket leaves ValidIssuer null and validates iss against the realm's OIDC discovery
        // issuer; pinning ValidIssuer=the realm here is the equivalent check without a live
        // ConfigurationManager to fetch that discovery document.
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"{_baseUrl}/realms/{Realm}",
            ValidateAudience = true,
            ValidAudience = BasketAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            IssuerSigningKeys = new JsonWebKeySet(jwks).GetSigningKeys(),
        };

        return await new JsonWebTokenHandler().ValidateTokenAsync(exchangedToken, parameters);
    }

    private async Task<string> PostTokenFormAsync(string realm, Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await _http.PostAsync(
            $"{_baseUrl}/realms/{realm}/protocol/openid-connect/token",
            new FormUrlEncodedContent(form),
            ct);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return payload.GetProperty("access_token").GetString()!;
    }

    private sealed class StaticOptionsMonitor(ServiceAuthOptions current) : IOptionsMonitor<ServiceAuthOptions>
    {
        public ServiceAuthOptions CurrentValue => current;
        public ServiceAuthOptions Get(string? name) => current;
        public IDisposable? OnChange(Action<ServiceAuthOptions, string?> listener) => null;
    }

    private sealed class PlainHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? CapturedBearer { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CapturedBearer = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
