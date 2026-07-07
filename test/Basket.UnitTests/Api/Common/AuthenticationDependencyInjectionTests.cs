using Basket.Api.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Basket.UnitTests.Api.Common;

/// <summary>
/// Direct coverage for the deployed-environment <c>PostConfigure&lt;JwtBearerOptions&gt;</c>
/// guard inside <see cref="AuthenticationDependencyInjection.AddBasketAuthentication"/>.
/// Exposed as a static helper so the security invariants can be exercised without
/// standing up the ASP.NET options pipeline.
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
    [Trait("Category", "regression")]
    public void AssertDeployedJwtBearerOptions_WhenRequireHttpsMetadataFalse_Throws()
    {
        // sum1.HIGH-3 guard: appsettings.json ships RequireHttpsMetadata=false for local
        // dev. If a deployed environment forgets to override (or an env-var injection
        // flips it back), the JWT signing-key discovery (OIDC metadata endpoint) is
        // served over plain HTTP — a real downgrade-attack vector. Make the guard
        // refuse to construct the host with this combination.

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
