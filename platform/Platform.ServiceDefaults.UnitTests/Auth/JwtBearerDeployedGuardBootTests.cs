using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Platform.ServiceDefaults.Auth;

namespace Platform.ServiceDefaults.UnitTests.Auth;

/// <summary>
/// Boot-time coverage for the deployed-environment <c>RequireHttpsMetadata</c> guard that
/// <see cref="JwtBearerConfigurator.AddPlatformJwtBearer"/> wires for <b>every</b> inbound-JWT edge.
/// The guard lives once at the platform layer (not per-BC), so a misconfigured deployed host
/// <b>refuses to construct</b> via <c>ValidateOnStart</c> rather than 500-ing per authenticated
/// request (named <c>JwtBearerOptions</c> otherwise materialize lazily on that first request).
/// Boots a real headless host so the wiring — not just a predicate — is exercised: dropping either
/// the guard or its <c>ValidateOnStart</c> makes the deployed-plaintext case boot cleanly and fail
/// this test. The env-gate is pinned too (Testing must boot untouched) so the guard can never
/// regress into failing the whole test suite at boot.
/// </summary>
public class JwtBearerDeployedGuardBootTests
{
    [Fact]
    [Trait("Category", "security")]
    public async Task AddPlatformJwtBearer_WhenDeployedAndRequireHttpsMetadataFalse_HostFailsToStart()
    {
        // Act
        var boot = async () =>
        {
            using var host = BuildHost(environmentName: "Staging", requireHttpsMetadata: false);
            await host.StartAsync(TestContext.Current.CancellationToken);
        };

        // Assert
        (await boot.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*HTTPS metadata*");
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task AddPlatformJwtBearer_WhenDeployedAndRequireHttpsMetadataTrue_HostStarts()
    {
        // Forcing JwtBearerOptions materialization at boot (ValidateOnStart) must not break a
        // correctly-configured deployed host: RequireHttpsMetadata=true plus an https authority
        // leaves the framework's own metadata-address guard satisfied, so the boot succeeds.

        // Act
        var boot = async () =>
        {
            using var host = BuildHost(environmentName: "Staging", requireHttpsMetadata: true);
            await host.StartAsync(TestContext.Current.CancellationToken);
            await host.StopAsync(TestContext.Current.CancellationToken);
        };

        // Assert
        await boot.Should().NotThrowAsync();
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task AddPlatformJwtBearer_WhenTestingEnvironmentAndRequireHttpsMetadataFalse_HostStarts()
    {
        // The guard is gated on IsDeployedEnvironment(): Development/Testing hosts ship
        // RequireHttpsMetadata=false for a local http Keycloak and must boot untouched. This
        // pins the env-gate so folding the guard into the platform can never fail every
        // in-memory test host at boot.

        // Act
        var boot = async () =>
        {
            using var host = BuildHost(environmentName: "Testing", requireHttpsMetadata: false);
            await host.StartAsync(TestContext.Current.CancellationToken);
            await host.StopAsync(TestContext.Current.CancellationToken);
        };

        // Assert
        await boot.Should().NotThrowAsync();
    }

    /// <summary>
    /// Builds a headless host wired through <see cref="JwtBearerConfigurator.AddPlatformJwtBearer"/>
    /// in <paramref name="environmentName"/>. <see cref="ServiceAuthOptions"/> seeds Authority /
    /// ValidIssuer inside the platform Configure step; the https authority keeps the framework's own
    /// metadata-address guard satisfied so <paramref name="requireHttpsMetadata"/> is the single knob
    /// under test. The configure delegate stands in for a BC's appsettings bind — including flipping
    /// <c>RequireHttpsMetadata</c> back to the local-dev default, the exact production foot-gun.
    /// </summary>
    private static IHost BuildHost(string environmentName, bool requireHttpsMetadata)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environmentName,
        });

        builder.Services.Configure<ServiceAuthOptions>(options =>
        {
            options.Authority = "https://id.example.test/realms/dotnetatlas";
            options.ClientId = "platform-tests";
            options.ClientSecret = "dev-secret";
            options.ServiceName = "platform-tests-service";
        });

        builder.Services.AddPlatformJwtBearer(options =>
        {
            options.RequireHttpsMetadata = requireHttpsMetadata;
            options.TokenValidationParameters.ValidAudience = "platform-tests-service";
        });

        return builder.Build();
    }
}
