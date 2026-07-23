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
                inboundAuthority: "http://id.example.test/realms/dotnetatlas");
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
        // and docker-compose boot (base appsettings ships RequireHttpsMetadata=true and no Authority;
        // the appsettings.Development.json overlay both relaxes the flag and supplies the dev http Authority).

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

    [Fact]
    [Trait("Category", "security")]
    public async Task AddPlatformJwtBearer_WhenDeployedAndAuthorityMissing_HostFailsToStart()
    {
        // The framework's own metadata-address guard only fires when an Authority is PRESENT but
        // plaintext — with no Authority at all it builds no ConfigurationManager, boots cleanly, and
        // defers failure to the first authenticated request. Base appsettings is deployment-shaped and
        // ships no Authority, so the ordinary forgot-to-override case — a deployed host whose config omits
        // the key (or explicitly blanks it, as simulated here) — is caught by this guard; the framework
        // check covers only the narrower case of an explicit plaintext Authority. Either way that host
        // fails closed at boot, not on 500s.

        // Act
        var boot = async () =>
        {
            using var host = BuildHost(environmentName: "Staging", requireHttpsMetadata: true, inboundAuthority: "");
            await host.StartAsync(TestContext.Current.CancellationToken);
        };

        // Assert
        (await boot.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Authority*");
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task AddPlatformJwtBearer_WhenTestingEnvironmentAndAuthorityMissing_HostStarts()
    {
        // Env-gate for the Authority guard. The nine fixtures that call ConfigureJwtBearerForTests
        // deliberately CLEAR Authority and MetadataAddress to stop the handler reaching for a
        // non-existent Keycloak, so enforcing presence outside deployed environments would fail
        // every one of them at boot.

        // Act
        var boot = async () =>
        {
            using var host = BuildHost(environmentName: "Testing", requireHttpsMetadata: false, inboundAuthority: "");
            await host.StartAsync(TestContext.Current.CancellationToken);
            await host.StopAsync(TestContext.Current.CancellationToken);
        };

        // Assert
        await boot.Should().NotThrowAsync();
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task AddPlatformJwtBearer_WhenDeployedAndOnlyOutboundServiceAuthAuthoritySet_HostFailsToStart()
    {
        // The inbound Authority guard must NOT be satisfiable by the outbound ServiceAuthOptions.Authority
        // (the realm this service fetches its own client-credentials token from). A deployed host that
        // binds no inbound Authority but carries an https outbound authority must still fail closed at
        // boot — proving the inbound trust anchor is sourced from Authentication:JwtBearer, not from the
        // service's outbound identity. If the outbound https authority could satisfy the inbound guard,
        // this host would boot and silently accept whatever realm ServiceAuth points at — the hazard
        // this test pins shut.

        // Act
        var boot = async () =>
        {
            using var host = BuildHost(
                environmentName: "Staging",
                requireHttpsMetadata: true,
                inboundAuthority: null,
                serviceAuthAuthority: "https://outbound.example/realms/some-other-realm");
            await host.StartAsync(TestContext.Current.CancellationToken);
        };

        // Assert
        (await boot.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Authority*");
    }

    /// <summary>
    /// Builds a headless host wired through <see cref="JwtBearerConfigurator.AddPlatformJwtBearer"/>
    /// in <paramref name="environmentName"/>. <paramref name="inboundAuthority"/> is fed through the
    /// configure delegate — the production path, standing in for a BC's <c>Authentication:JwtBearer</c>
    /// appsettings bind; <c>null</c> binds no inbound Authority at all (the deployment-shaped base that
    /// ships no dev-only http Authority). An https authority keeps the framework's own metadata-address
    /// guard satisfied so <paramref name="requireHttpsMetadata"/> is the knob under test, while an http
    /// authority exercises that framework guard. <paramref name="serviceAuthAuthority"/> populates the
    /// OUTBOUND <see cref="ServiceAuthOptions.Authority"/> only — it must never influence inbound
    /// validation. The delegate also flips <c>RequireHttpsMetadata</c> back to the caller's value, the
    /// exact production foot-gun a BC's bind can reintroduce.
    /// </summary>
    private static IHost BuildHost(
        string environmentName,
        bool requireHttpsMetadata,
        string? inboundAuthority = "https://id.example.test/realms/dotnetatlas",
        string? serviceAuthAuthority = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environmentName,
        });

        if (serviceAuthAuthority is not null)
        {
            builder.Services.Configure<ServiceAuthOptions>(options =>
            {
                options.Authority = serviceAuthAuthority;
                options.ClientId = "platform-tests";
                options.ClientSecret = "dev-secret";
                options.ServiceName = "platform-tests-service";
            });
        }

        builder.Services.AddPlatformJwtBearer(options =>
        {
            options.RequireHttpsMetadata = requireHttpsMetadata;
            options.TokenValidationParameters.ValidAudience = "platform-tests-service";
            if (inboundAuthority is not null)
            {
                options.Authority = inboundAuthority;
            }
        });

        return builder.Build();
    }
}
