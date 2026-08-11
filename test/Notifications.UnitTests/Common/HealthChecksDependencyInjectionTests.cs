using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Notifications.Infrastructure.Common;
using Platform.ServiceDefaults.Config;

namespace Notifications.UnitTests.Common;

/// <summary>
/// Pins which tag each Notifications health check carries. Rationale for the liveness/readiness
/// split: <see cref="ServiceDefaultHealthCheckTags.LivenessTag"/>.
/// </summary>
public class HealthChecksDependencyInjectionTests
{
    [Fact]
    public void AddNotificationsHealthChecks_TagsNothingForLiveness()
    {
        var registrations = RegisterHealthChecks();

        registrations
            .Where(registration => registration.Tags.Contains(ServiceDefaultHealthCheckTags.LivenessTag))
            .Select(registration => registration.Name)
            .Should().BeEmpty("Notifications has no check that a restart could fix");
    }

    [Fact]
    public void AddNotificationsHealthChecks_TagsEveryDependencyForReadiness()
    {
        var registrations = RegisterHealthChecks();

        registrations
            .Where(registration => registration.Tags.Contains(ServiceDefaultHealthCheckTags.ReadinessTag))
            .Select(registration => registration.Name)
            .Should().BeEquivalentTo(
                ["ApplicationLifecycle", "Notifications DB", "Kafka"],
                "readiness is the declared dependency set; Notifications uses no Redis, and the " +
                "SMTP relay is not a readiness gate");
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
        services.AddNotificationsHealthChecks(configuration);

        using var provider = services.BuildServiceProvider();
        return [.. provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations];
    }
}
