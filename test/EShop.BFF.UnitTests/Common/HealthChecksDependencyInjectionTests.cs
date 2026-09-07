using EShop.BFF.Infrastructure.Caching;
using EShop.BFF.Infrastructure.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults.Config;

namespace EShop.BFF.UnitTests.Common;

/// <summary>
/// Pins which tag each BFF health check carries, and the status a failing <c>redis-cache</c>
/// reports. Rationale for the liveness/readiness split:
/// <see cref="ServiceDefaultHealthCheckTags.LivenessTag"/>.
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
                ["ApplicationLifecycle", "redis-cache", "Kafka topics"],
                "readiness is the declared dependency set; the upstream BCs and the Kafka broker " +
                "are deliberately absent because probing them would couple the BFF's availability " +
                "to theirs. \"Kafka topics\" does not couple it either: it contacts no broker once " +
                "the subscribed topics have been verified");
    }

    /// <summary>
    /// <c>AddRedis</c>'s check returns <c>context.Registration.FailureStatus</c> on every failure
    /// path, so the registration argument is what decides. Nothing else reads the value back, so a
    /// wrong one is silent until an outage.
    /// </summary>
    [Fact]
    public void AddBffHealthChecks_RegistersRedisCacheAsDegradedOnFailure()
    {
        RegisterHealthChecks()
            .Single(registration => registration.Name == "redis-cache")
            .FailureStatus
            .Should().Be(
                HealthStatus.Degraded,
                "the BFF serves uncached without redis-cache, so readiness stays 200 — see ADR-0016");
    }

    private static IReadOnlyCollection<HealthCheckRegistration> RegisterHealthChecks()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HealthChecks:RedisTimeout"] = "00:00:01",
            [$"ConnectionStrings:{BffCacheConstants.RedisCacheConnectionStringName}"] = "localhost:6379",
            ["Kafka:Brokers:0"] = "localhost:9092",
            ["Topics:CatalogProducts"] = "catalog.products",
            ["Topics:CatalogCategories"] = "catalog.categories",
            ["Topics:InventoryStockEvents"] = "inventory.stock-events",
            ["Topics:BasketSessions"] = "basket.sessions",
        });

        var services = new ServiceCollection();
        services.AddBffHealthChecks(configuration);

        using var provider = services.BuildServiceProvider();
        return [.. provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations];
    }
}
