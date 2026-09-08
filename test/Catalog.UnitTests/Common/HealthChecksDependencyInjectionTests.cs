using Catalog.Infrastructure.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults.Config;
using Platform.ServiceDefaults.Idempotency;

namespace Catalog.UnitTests.Common;

/// <summary>
/// Pins which tag each Catalog health check carries, and the status a failing <c>Kafka</c> reports.
/// Rationale for the liveness/readiness split:
/// <see cref="ServiceDefaultHealthCheckTags.LivenessTag"/>.
/// </summary>
public class HealthChecksDependencyInjectionTests
{
    [Fact]
    public void AddCatalogHealthChecks_TagsNothingForLiveness()
    {
        var registrations = RegisterHealthChecks();

        registrations
            .Where(registration => registration.Tags.Contains(ServiceDefaultHealthCheckTags.LivenessTag))
            .Select(registration => registration.Name)
            .Should().BeEmpty("Catalog has no check that a restart could fix");
    }

    [Fact]
    public void AddCatalogHealthChecks_TagsEveryDependencyForReadiness()
    {
        var registrations = RegisterHealthChecks();

        registrations
            .Where(registration => registration.Tags.Contains(ServiceDefaultHealthCheckTags.ReadinessTag))
            .Select(registration => registration.Name)
            .Should().BeEquivalentTo(
                ["ApplicationLifecycle", "Catalog DB", "redis-cache", "Kafka", "Kafka topics"],
                "readiness is the declared dependency set; the Schema Registry is deliberately " +
                "absent because it is contacted cold-cache only");
    }

    /// <summary>
    /// <c>AddKafka</c>'s check returns <c>context.Registration.FailureStatus</c> on every failure
    /// path, so the registration argument is what decides. Confirmed by running the registered check
    /// against an unreachable broker while writing this; nothing reads the value back afterwards, so
    /// a wrong one is silent until an outage.
    /// </summary>
    [Fact]
    public void AddCatalogHealthChecks_RegistersKafkaAsDegradedOnFailure()
    {
        RegisterHealthChecks()
            .Single(registration => registration.Name == "Kafka")
            .FailureStatus
            .Should().Be(
                HealthStatus.Degraded,
                "no Catalog request path publishes, so the HTTP surface serves with the broker " +
                "down — only consumers stall, and readiness gates HTTP routing, so de-rotating " +
                "would unblock nothing");
    }

    private static IReadOnlyCollection<HealthCheckRegistration> RegisterHealthChecks()
    {
        var services = new ServiceCollection();
        services.AddCatalogHealthChecks(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        return [.. provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations];
    }

    private static ConfigurationManager BuildConfiguration(string brokers = "localhost:9092")
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HealthChecks:DbTimeout"] = "00:00:01",
            // Bounds the unreachable-broker probe above: the registration derives
            // MessageTimeoutMs from this, and librdkafka retries metadata until it elapses.
            ["HealthChecks:KafkaTimeout"] = "00:00:02",
            ["HealthChecks:RedisTimeout"] = "00:00:01",
            ["Kafka:Brokers:0"] = brokers,
            [$"ConnectionStrings:{IdempotencyKeyServiceCollectionExtensions.RedisConnectionStringName}"] =
                "localhost:6379",
            ["Topics:CatalogProducts"] = "catalog.products",
            ["Topics:CatalogCategories"] = "catalog.categories",
            ["Topics:InventoryStockEvents"] = "inventory.stock-events",
            ["Topics:DltTopicSuffix"] = ".Catalog.DLT",
        });

        return configuration;
    }
}
