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
/// this test. Both disjuncts of the env-gate are pinned too (Development and Testing must boot
/// untouched) so the guard can never regress into failing every laptop run and test host at boot.
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

    [Fact]
    [Trait("Category", "security")]
    public async Task AddPlatformJwtBearer_WhenDeployedAndHttpsRequiredButAuthorityIsHttp_HostFailsToStart()
    {
        // RequireHttpsMetadata=true but an http:// Authority: the platform guard passes (it only
        // trips on RequireHttpsMetadata=false), so the framework's own JwtBearerPostConfigureOptions
        // is what rejects the plaintext metadata address — and, because ValidateOnStart forces
        // materialization at boot, it does so at startup, not lazily on the first request. Pins the
        // ADR-0009 item 10 claim that this second misconfig also fails closed at boot. The framework's
        // distinct "*must use HTTPS*" message (vs the platform guard's "*HTTPS metadata*") proves it
        // is the framework guard firing here, not ours.

        // Act
        var boot = async () =>
        {
            using var host = BuildHost(
                environmentName: "Staging",
                requireHttpsMetadata: true,
                authority: "http://id.example.test/realms/dotnetatlas");
            await host.StartAsync(TestContext.Current.CancellationToken);
        };

        // Assert
        (await boot.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*must use HTTPS*");
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task AddPlatformJwtBearer_WhenDevelopmentEnvironmentAndRequireHttpsMetadataFalse_HostStarts()
    {
        // Env-gate, IsDevelopment() disjunct — the second half of
        // IsDeployedEnvironment() = !(IsDevelopment() || IsTesting()). Without this case a mutation
        // dropping IsDevelopment() survives the whole suite, yet would fail every laptop `dotnet run`
        // and docker-compose boot (base appsettings ships RequireHttpsMetadata=false + http Authority).

        // Act
        var boot = async () =>
        {
            using var host = BuildHost(environmentName: "Development", requireHttpsMetadata: false);
            await host.StartAsync(TestContext.Current.CancellationToken);
            await host.StopAsync(TestContext.Current.CancellationToken);
        };

        // Assert
        await boot.Should().NotThrowAsync();
    }

    /// <summary>
    /// Builds a headless host wired through <see cref="JwtBearerConfigurator.AddPlatformJwtBearer"/>
    /// in <paramref name="environmentName"/>. <see cref="ServiceAuthOptions"/> seeds
    /// <paramref name="authority"/> (the JwtBearer Authority / ValidIssuer) inside the platform
    /// Configure step; an https authority keeps the framework's own metadata-address guard satisfied
    /// so <paramref name="requireHttpsMetadata"/> is the knob under test, while an http authority
    /// exercises that framework guard. The configure delegate stands in for a BC's appsettings bind —
    /// including flipping <c>RequireHttpsMetadata</c> back to the local-dev default, the exact
    /// production foot-gun.
    /// </summary>
    private static IHost BuildHost(
        string environmentName,
        bool requireHttpsMetadata,
        string authority = "https://id.example.test/realms/dotnetatlas")
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environmentName,
        });

        builder.Services.Configure<ServiceAuthOptions>(options =>
        {
            options.Authority = authority;
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
