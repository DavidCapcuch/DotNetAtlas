namespace SagaOrchestrators.Common.Config;

/// <summary>
/// Health check timeout configuration.
/// </summary>
public sealed class HealthCheckTimeoutsOptions
{
    public const string Section = "HealthChecks";

    /// <summary>
    /// Timeout for the self health check.
    /// </summary>
    public TimeSpan SelfTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Timeout for the Kafka health check.
    /// </summary>
    public TimeSpan KafkaTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
