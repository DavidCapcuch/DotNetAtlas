using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Ordering.Infrastructure.Common;
using Platform.ServiceDefaults.Config;
using Platform.ServiceDefaults.Idempotency;

namespace Ordering.UnitTests.Common;

/// <summary>
/// Pins which tag each Ordering health check carries, and the status a failing
/// Kafka reports. Rationale for the liveness/readiness
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
                ["ApplicationLifecycle", "Ordering DB", "redis-cache", "Kafka", "Kafka topics"],
                "readiness is the declared dependency set; the Schema Registry is deliberately " +
                "absent because it is contacted cold-cache only");
    }

    [Fact]
    public void AddOrderingHealthChecks_RegistersKafkaAsDegradedOnFailure()
    {
        RegisterHealthChecks()
            .Single(registration => registration.Name == "Kafka")
            .FailureStatus
            .Should().Be(
                HealthStatus.Degraded,
                "no Ordering request path publishes, so the HTTP surface serves with the broker " +
                "down — only the saga-command consumer stalls, and readiness gates HTTP routing, " +
                "so de-rotating would unblock nothing");
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
            ["Topics:OrderingOrders"] = "ordering.orders",
            ["Topics:OrderCommands"] = "ordering.order-commands",
            ["Topics:DltTopicSuffix"] = ".Ordering.DLT",
        });

        var services = new ServiceCollection();
        services.AddOrderingHealthChecks(configuration);

        using var provider = services.BuildServiceProvider();
        return [.. provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations];
    }
}
