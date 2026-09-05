using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Ordering.Infrastructure.Common;
using Platform.ServiceDefaults.Config;
using Platform.ServiceDefaults.Idempotency;

namespace Ordering.UnitTests.Common;

/// <summary>
/// Pins which tag each Ordering health check carries. Rationale for the liveness/readiness
/// split: <see cref="ServiceDefaultHealthCheckTags.LivenessTag"/>.
/// </summary>
public class HealthChecksDependencyInjectionTests
{
    [Fact]
    public void AddOrderingHealthChecks_TagsNothingForLiveness()
    {
        var registrations = RegisterHealthChecks();

        registrations
            .Where(registration => registration.Tags.Contains(ServiceDefaultHealthCheckTags.LivenessTag))
            .Select(registration => registration.Name)
            .Should().BeEmpty("Ordering has no check that a restart could fix");
    }

    [Fact]
    public void AddOrderingHealthChecks_TagsEveryDependencyForReadiness()
    {
        var registrations = RegisterHealthChecks();

        registrations
            .Where(registration => registration.Tags.Contains(ServiceDefaultHealthCheckTags.ReadinessTag))
            .Select(registration => registration.Name)
            .Should().BeEquivalentTo(
                ["ApplicationLifecycle", "Ordering DB", "redis-cache", "Kafka"],
                "readiness is the declared dependency set; the Schema Registry is deliberately " +
                "absent because it is contacted cold-cache only");
    }

    private static IReadOnlyCollection<HealthCheckRegistration> RegisterHealthChecks()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HealthChecks:DbTimeout"] = "00:00:01",
            ["HealthChecks:KafkaTimeout"] = "00:00:02",
            ["HealthChecks:RedisTimeout"] = "00:00:01",
            ["Kafka:Brokers:0"] = "localhost:9092",
            [$"ConnectionStrings:{IdempotencyKeyServiceCollectionExtensions.RedisConnectionStringName}"] =
                "localhost:6379",
        });

        var services = new ServiceCollection();
        services.AddOrderingHealthChecks(configuration);

        using var provider = services.BuildServiceProvider();
        return [.. provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations];
    }
}
