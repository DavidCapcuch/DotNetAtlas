using NSwag;
using Platform.Api.Swagger;

namespace Platform.Api.UnitTests;

/// <summary>
/// Pins <see cref="SwaggerDependencyInjection.BuildOAuth2Scheme"/>, the extracted seam that decides
/// whether the Swagger document carries a Keycloak OAuth2 Authorize button. With an authority it
/// derives the Authorization-Code endpoints; without one it returns <c>null</c> so document
/// generation degrades gracefully (doc served, no Authorize button) instead of throwing — the
/// robustness the deployment-shaped base needs when it drops <c>Authentication:JwtBearer:Authority</c>.
/// </summary>
public class SwaggerOAuth2SchemeTests
{
    private const string Authority = "http://localhost:9011/realms/dotnetatlas";

    private static readonly IReadOnlyDictionary<string, string> Scopes = new Dictionary<string, string>
    {
        ["openid"] = "OpenID.",
        ["profile"] = "Profile.",
    };

    [Fact]
    public void BuildOAuth2Scheme_WithAuthority_DerivesAuthorizationCodeFlowFromIt()
    {
        var scheme = SwaggerDependencyInjection.BuildOAuth2Scheme(Authority, Scopes);

        using (new AssertionScope())
        {
            scheme.Should().NotBeNull();
            scheme!.Type.Should().Be(OpenApiSecuritySchemeType.OAuth2);

            var flow = scheme.Flows.AuthorizationCode;
            flow.AuthorizationUrl.Should().Be($"{Authority}/protocol/openid-connect/auth");
            flow.TokenUrl.Should().Be($"{Authority}/protocol/openid-connect/token");
            flow.Scopes.Should().ContainKey("openid");
            flow.Scopes["openid"].Should().Be("OpenID.");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildOAuth2Scheme_WithoutAuthority_ReturnsNullSoTheDocGeneratesWithoutAuthorize(string? authority)
    {
        var scheme = SwaggerDependencyInjection.BuildOAuth2Scheme(authority, Scopes);

        scheme.Should().BeNull();
    }
}
