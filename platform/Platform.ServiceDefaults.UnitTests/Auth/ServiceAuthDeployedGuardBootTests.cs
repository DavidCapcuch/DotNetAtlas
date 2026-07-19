using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults.Auth;

namespace Platform.ServiceDefaults.UnitTests.Auth;

/// <summary>
/// Boot-time coverage for the deployed-environment <c>ServiceAuth:Authority</c> HTTPS guard that
/// <see cref="ServiceAuthServiceCollectionExtensions.AddServiceAuth(IServiceCollection, string)"/>
/// wires for every <b>outbound-active</b> edge. A plaintext <c>http://</c> Authority in a deployed
/// host would POST the client-credentials <c>client_secret</c> and RFC 8693 exchanged user tokens to
/// Keycloak over the wire — the symmetric MITM surface to the inbound
/// <see cref="JwtBearerConfigurator"/> guard (ADR-0009 item 10). The guard rides the existing
/// <c>AddOptionsWithValidateOnStart&lt;ServiceAuthOptions&gt;</c> chain, so a misconfigured deployed
/// host <b>refuses to start</b> rather than leaking the secret on the first outbound call.
/// Boots a real headless host so the wiring — not just a predicate — is exercised; the env-gate is
/// pinned on both disjuncts (Development and Testing must boot untouched against local http Keycloak)
/// so the guard can never regress into failing the whole suite at boot.
/// </summary>
public class ServiceAuthDeployedGuardBootTests
{
    private const string HttpAuthority = "http://localhost:9011/realms/dotnetatlas";
    private const string HttpsAuthority = "https://id.example.test/realms/dotnetatlas";

    [Fact]
    [Trait("Category", "security")]
    public async Task AddServiceAuth_WhenDeployedAndAuthorityIsPlaintextHttp_HostFailsToStart()
    {
        // Act
        var boot = async () =>
        {
            using var host = BuildHost(environmentName: "Staging", authority: HttpAuthority);
            await host.StartAsync(TestContext.Current.CancellationToken);
        };

        // Assert — ValidateOnStart surfaces the guard as OptionsValidationException at boot.
        (await boot.Should().ThrowAsync<OptionsValidationException>())
            .WithMessage("*https*");
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task AddServiceAuth_WhenDeployedAndAuthorityIsMalformedButHttpsPrefixed_HostFailsToStart()
    {
        // A scheme-less authority that nonetheless begins with the literal "https" (a dropped "://").
        // ValidateDataAnnotations lets it through — [Required] rejects only empty — so this guard is the
        // only thing that fails it closed. The value is chosen to separate parsing from prefix-matching:
        // a StartsWith("https") implementation would admit it and boot, so this pins the absolute-URI
        // parse that IsHttpsAuthority documents.

        // Act
        var boot = async () =>
        {
            using var host = BuildHost(environmentName: "Staging", authority: "https.example.test/realms/dotnetatlas");
            await host.StartAsync(TestContext.Current.CancellationToken);
        };

        // Assert
        (await boot.Should().ThrowAsync<OptionsValidationException>())
            .WithMessage("*https*");
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task AddServiceAuth_WhenDeployedAndAuthorityIsHttps_HostStarts()
    {
        // The guard must admit a correctly-configured deployed host: an https Authority passes, so
        // forcing ServiceAuthOptions materialization at boot (ValidateOnStart) does not break it.

        // Act
        var boot = async () =>
        {
            using var host = BuildHost(environmentName: "Staging", authority: HttpsAuthority);
            await host.StartAsync(TestContext.Current.CancellationToken);
            await host.StopAsync(TestContext.Current.CancellationToken);
        };

        // Assert
        await boot.Should().NotThrowAsync();
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task AddServiceAuth_WhenTestingEnvironmentAndAuthorityIsPlaintextHttp_HostStarts()
    {
        // Env-gate, IsTesting() disjunct: test fixtures run against local http Keycloak and must boot
        // untouched. Pins that folding the guard into the platform can never fail every in-memory
        // test host at boot.

        // Act
        var boot = async () =>
        {
            using var host = BuildHost(environmentName: "Testing", authority: HttpAuthority);
            await host.StartAsync(TestContext.Current.CancellationToken);
            await host.StopAsync(TestContext.Current.CancellationToken);
        };

        // Assert
        await boot.Should().NotThrowAsync();
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task AddServiceAuth_WhenDevelopmentEnvironmentAndAuthorityIsPlaintextHttp_HostStarts()
    {
        // Env-gate, IsDevelopment() disjunct: laptop `dotnet run` + docker-compose talk to Keycloak
        // over http on localhost:9011 and must boot untouched.

        // Act
        var boot = async () =>
        {
            using var host = BuildHost(environmentName: "Development", authority: HttpAuthority);
            await host.StartAsync(TestContext.Current.CancellationToken);
            await host.StopAsync(TestContext.Current.CancellationToken);
        };

        // Assert
        await boot.Should().NotThrowAsync();
    }

    /// <summary>
    /// Builds a headless host wired through
    /// <see cref="ServiceAuthServiceCollectionExtensions.AddServiceAuth(IServiceCollection, string)"/>
    /// in <paramref name="environmentName"/>. <c>ServiceAuth</c> is seeded via configuration (the guard
    /// reads <c>Authority</c> from <c>BindConfiguration</c>), with the remaining Required fields set so
    /// <c>ValidateDataAnnotations</c> passes and <paramref name="authority"/> is the single knob under test.
    /// </summary>
    private static IHost BuildHost(string environmentName, string authority)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environmentName,
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceAuth:Authority"] = authority,
            ["ServiceAuth:ClientId"] = "platform-tests",
            ["ServiceAuth:ClientSecret"] = "dev-secret",
            ["ServiceAuth:ServiceName"] = "platform-tests-service",
        });

        // ClientCredentialsTokenHandler depends on TimeProvider (AddServiceDefaults registers it
        // platform-wide); Development's ValidateOnBuild eagerly resolves it at Build().
        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddServiceAuth("platform-tests-service");

        return builder.Build();
    }
}
