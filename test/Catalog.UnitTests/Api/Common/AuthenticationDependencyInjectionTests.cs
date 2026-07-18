using Catalog.Api.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Catalog.UnitTests.Api.Common;

/// <summary>
/// Direct coverage for the deployed-environment <c>PostConfigure&lt;JwtBearerOptions&gt;</c>
/// guard inside <see cref="AuthenticationDependencyInjection.AddCatalogAuthentication"/>.
/// Exposed as a static helper so the security invariants can be exercised without
/// standing up the ASP.NET options pipeline (which only wires the guard in a deployed
/// environment, never under the Development/Testing hosts the test suite boots in).
/// </summary>
public class AuthenticationDependencyInjectionTests
{
    [Fact]
    [Trait("Category", "security")]
    public void AssertDeployedJwtBearerOptions_WhenAllStrictFlagsTrue_DoesNotThrow()
    {
        // Arrange
        var options = MakeOptions(requireSigned: true, validateSigningKey: true, requireHttps: true);

        // Act
        var act = () => AuthenticationDependencyInjection.AssertDeployedJwtBearerOptions(options);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    [Trait("Category", "security")]
    public void AssertDeployedJwtBearerOptions_WhenRequireSignedTokensFalse_Throws()
    {
        // Arrange
        var options = MakeOptions(requireSigned: false, validateSigningKey: true, requireHttps: true);

        // Act
        var act = () => AuthenticationDependencyInjection.AssertDeployedJwtBearerOptions(options);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    [Trait("Category", "security")]
    public void AssertDeployedJwtBearerOptions_WhenValidateIssuerSigningKeyFalse_Throws()
    {
        // Arrange
        var options = MakeOptions(requireSigned: true, validateSigningKey: false, requireHttps: true);

        // Act
        var act = () => AuthenticationDependencyInjection.AssertDeployedJwtBearerOptions(options);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    [Trait("Category", "security")]
    public void AssertDeployedJwtBearerOptions_WhenRequireHttpsMetadataFalse_Throws()
    {
        // appsettings.json ships RequireHttpsMetadata=false for local dev, and the platform
        // JwtBearer PostConfigure re-pins only the TokenValidationParameters booleans — not
        // RequireHttpsMetadata. So a deployed environment that inherits that default without an
        // explicit override would fetch OIDC signing-key metadata over plain HTTP: a real
        // downgrade-attack vector. This guard is the only line that catches it — it throws for
        // this combination so a misconfigured deployed host fails closed rather than fetching
        // signing keys over plain HTTP.

        // Arrange
        var options = MakeOptions(requireSigned: true, validateSigningKey: true, requireHttps: false);

        // Act
        var act = () => AuthenticationDependencyInjection.AssertDeployedJwtBearerOptions(options);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    private static JwtBearerOptions MakeOptions(bool requireSigned, bool validateSigningKey, bool requireHttps) => new()
    {
        RequireHttpsMetadata = requireHttps,
        TokenValidationParameters = new TokenValidationParameters
        {
            RequireSignedTokens = requireSigned,
            ValidateIssuerSigningKey = validateSigningKey,
        },
    };
}
