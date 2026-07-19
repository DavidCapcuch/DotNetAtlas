using Catalog.Api.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Catalog.UnitTests.Api.Common;

/// <summary>
/// Boot-time coverage for the deployed-environment JwtBearer guard wired by
/// <see cref="AuthenticationDependencyInjection.AddCatalogAuthentication"/>. Unlike the
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
    public async Task AddCatalogAuthentication_WhenDeployedAndRequireHttpsMetadataFalse_HostFailsToStart()
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
    public async Task AddCatalogAuthentication_WhenDeployedAndConfiguredCorrectly_HostStarts()
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
    /// <see cref="AuthenticationDependencyInjection.AddCatalogAuthentication"/>. An https JwtBearer
    /// authority keeps the framework's own metadata-address guard satisfied, so the deployed JWT
    /// guard's <c>RequireHttpsMetadata</c> assertion is the only invariant that can fail the boot —
    /// <paramref name="requireHttpsMetadata"/> is the single knob under test. Catalog has no
    /// outbound <c>ServiceAuth</c> registration, so none is configured here.
    /// </summary>
    private static IHost BuildDeployedHost(bool requireHttpsMetadata)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = "Staging",
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:JwtBearer:Authority"] = "https://id.example.test/realms/dotnetatlas",
            ["Authentication:JwtBearer:RequireHttpsMetadata"] = requireHttpsMetadata ? "true" : "false",
        });

        builder.Services.AddCatalogAuthentication(builder.Configuration);

        return builder.Build();
    }
}
