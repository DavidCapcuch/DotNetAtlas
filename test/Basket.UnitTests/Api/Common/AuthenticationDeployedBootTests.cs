using Basket.Api.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Basket.UnitTests.Api.Common;

/// <summary>
/// Boot-time coverage for the deployed-environment JwtBearer guard wired by
/// <see cref="AuthenticationDependencyInjection.AddBasketAuthentication"/>. Unlike the
/// isolated-predicate tests, these boot a real (headless) host in a deployed environment and
/// assert the guard is evaluated by <c>ValidateOnStart</c> at startup — so a misconfigured
/// deployed host <b>refuses to construct</b> rather than 500-ing per authenticated request.
/// Pins the wiring: dropping either <c>.ValidateOnStart()</c> or the <c>.PostConfigure(...)</c>
/// guard makes the negative case boot cleanly and fail this test.
/// </summary>
public class AuthenticationDeployedBootTests
{
    [Fact]
    [Trait("Category", "security")]
    public async Task AddBasketAuthentication_WhenDeployedAndRequireHttpsMetadataFalse_HostFailsToStart()
    {
        // Act
        var boot = async () =>
        {
            using var host = BuildDeployedHost(requireHttpsMetadata: false);
            await host.StartAsync(TestContext.Current.CancellationToken);
        };

        // Assert
        (await boot.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*HTTPS metadata*");
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task AddBasketAuthentication_WhenDeployedAndConfiguredCorrectly_HostStarts()
    {
        // Forcing JwtBearerOptions materialization at boot (ValidateOnStart) must not break a
        // correctly-configured deployed host: RequireHttpsMetadata=true plus an https authority
        // leaves the framework's own metadata-address guard satisfied, so the boot succeeds.

        // Act
        var boot = async () =>
        {
            using var host = BuildDeployedHost(requireHttpsMetadata: true);
            await host.StartAsync(TestContext.Current.CancellationToken);
            await host.StopAsync(TestContext.Current.CancellationToken);
        };

        // Assert
        await boot.Should().NotThrowAsync();
    }

    /// <summary>
    /// Builds a headless host in a deployed ("Staging") environment wired through the real
    /// <see cref="AuthenticationDependencyInjection.AddBasketAuthentication"/>. Supplies a valid
    /// <c>ServiceAuth</c> section (its own <c>ValidateOnStart</c>) and an https JwtBearer authority
    /// so the deployed JWT guard's <c>RequireHttpsMetadata</c> assertion is the only invariant that
    /// can fail the boot — <paramref name="requireHttpsMetadata"/> is the single knob under test.
    /// </summary>
    private static IHost BuildDeployedHost(bool requireHttpsMetadata)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = "Staging",
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceAuth:Authority"] = "https://id.example.test/realms/dotnetatlas",
            ["ServiceAuth:ClientId"] = "basket-service",
            ["ServiceAuth:ClientSecret"] = "test-secret",
            ["Authentication:JwtBearer:Authority"] = "https://id.example.test/realms/dotnetatlas",
            ["Authentication:JwtBearer:RequireHttpsMetadata"] = requireHttpsMetadata ? "true" : "false",
        });

        builder.Services.AddBasketAuthentication(builder.Configuration, builder.Environment);

        return builder.Build();
    }
}
