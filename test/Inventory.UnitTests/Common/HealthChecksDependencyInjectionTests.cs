using Inventory.Infrastructure.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults.Config;
using Platform.ServiceDefaults.Idempotency;

namespace Inventory.UnitTests.Common;

/// <summary>
/// Pins which tag each Inventory health check carries. Rationale for the liveness/readiness
/// split: <see cref="ServiceDefaultHealthCheckTags.LivenessTag"/>.
/// </summary>
public class HealthChecksDependencyInjectionTests
{
    [Fact]
    public void AddInventoryHealthChecks_TagsNothingForLiveness()
    {
        var registrations = RegisterHealthChecks();

        registrations
            .Where(registration => registration.Tags.Contains(ServiceDefaultHealthCheckTags.LivenessTag))
            .Select(registration => registration.Name)
            .Should().BeEmpty("Inventory has no check that a restart could fix");
    }

    [Fact]
    public void AddInventoryHealthChecks_TagsEveryDependencyForReadiness()
    {
        var registrations = RegisterHealthChecks();

        registrations
            .Where(registration => registration.Tags.Contains(ServiceDefaultHealthCheckTags.ReadinessTag))
            .Select(registration => registration.Name)
            .Should().BeEquivalentTo(
                ["ApplicationLifecycle", "Inventory DB", "redis-cache", "Kafka"],
                "readiness is the declared dependency set; the Schema Registry is deliberately " +
                "absent because it is contacted cold-cache only");
    }

    private static IReadOnlyCollection<HealthCheckRegistration> RegisterHealthChecks()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HealthChecks:KafkaTimeout"] = "00:00:05",
            ["HealthChecks:RedisTimeout"] = "00:00:04",
            ["Kafka:Brokers:0"] = "localhost:9092",
            [$"ConnectionStrings:{IdempotencyKeyServiceCollectionExtensions.RedisConnectionStringName}"] =
                "localhost:6379",
        });

        var services = new ServiceCollection();
        services.AddInventoryHealthChecks(configuration);

        using var provider = services.BuildServiceProvider();
        return [.. provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations];
    }
}
