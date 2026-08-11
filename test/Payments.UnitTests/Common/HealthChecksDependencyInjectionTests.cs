using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Payments.Infrastructure.Common;
using Platform.ServiceDefaults.Config;

namespace Payments.UnitTests.Common;

/// <summary>
/// Pins which tag each Payments health check carries. Rationale for the liveness/readiness
/// split: <see cref="ServiceDefaultHealthCheckTags.LivenessTag"/>.
/// </summary>
public class HealthChecksDependencyInjectionTests
{
    [Fact]
    public void AddPaymentsHealthChecks_TagsNothingForLiveness()
    {
        var registrations = RegisterHealthChecks();

        registrations
            .Where(registration => registration.Tags.Contains(ServiceDefaultHealthCheckTags.LivenessTag))
            .Select(registration => registration.Name)
            .Should().BeEmpty("Payments has no check that a restart could fix");
    }

    [Fact]
    public void AddPaymentsHealthChecks_TagsEveryDependencyForReadiness()
    {
        var registrations = RegisterHealthChecks();

        registrations
            .Where(registration => registration.Tags.Contains(ServiceDefaultHealthCheckTags.ReadinessTag))
            .Select(registration => registration.Name)
            .Should().BeEquivalentTo(
                ["ApplicationLifecycle", "Payments DB", "Kafka"],
                "readiness is the declared dependency set; Payments uses no Redis, and the " +
                "external payment gateway is not a readiness gate");
    }

    private static IReadOnlyCollection<HealthCheckRegistration> RegisterHealthChecks()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HealthChecks:KafkaTimeout"] = "00:00:05",
            ["Kafka:Brokers:0"] = "localhost:9092",
        });

        var services = new ServiceCollection();
        services.AddPaymentsHealthChecks(configuration);

        using var provider = services.BuildServiceProvider();
        return [.. provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations];
    }
}
