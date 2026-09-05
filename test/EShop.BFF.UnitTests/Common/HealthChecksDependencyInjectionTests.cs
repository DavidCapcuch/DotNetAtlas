using EShop.BFF.Infrastructure.Caching;
using EShop.BFF.Infrastructure.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults.Config;

namespace EShop.BFF.UnitTests.Common;

/// <summary>
/// Pins which tag each BFF health check carries. Rationale for the liveness/readiness
/// split: <see cref="ServiceDefaultHealthCheckTags.LivenessTag"/>.
/// </summary>
public class HealthChecksDependencyInjectionTests
{
    [Fact]
    public void AddBffHealthChecks_TagsNothingForLiveness()
    {
        var registrations = RegisterHealthChecks();

        registrations
            .Where(registration => registration.Tags.Contains(ServiceDefaultHealthCheckTags.LivenessTag))
            .Select(registration => registration.Name)
            .Should().BeEmpty("the BFF holds no state of its own, so nothing here is restart-fixable");
    }

    [Fact]
    public void AddBffHealthChecks_TagsEveryDependencyForReadiness()
    {
        var registrations = RegisterHealthChecks();

        registrations
            .Where(registration => registration.Tags.Contains(ServiceDefaultHealthCheckTags.ReadinessTag))
            .Select(registration => registration.Name)
            .Should().BeEquivalentTo(
                ["ApplicationLifecycle", "redis-cache"],
                "readiness is the declared dependency set; the upstream BCs are deliberately " +
                "absent because probing them would couple the BFF's availability to theirs");
    }

    private static IReadOnlyCollection<HealthCheckRegistration> RegisterHealthChecks()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HealthChecks:RedisTimeout"] = "00:00:01",
            [$"ConnectionStrings:{BffCacheConstants.RedisCacheConnectionStringName}"] = "localhost:6379",
        });

        var services = new ServiceCollection();
        services.AddBffHealthChecks(configuration);

        using var provider = services.BuildServiceProvider();
        return [.. provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations];
    }
}
