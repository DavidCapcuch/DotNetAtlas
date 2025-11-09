namespace DotNetAtlas.OutboxRelay.WorkerService.Common;

public static class InfrastructureConstants
{
    private const string ApiBasePath = "/api";

    public const string HealthEndpointPath = $"{ApiBasePath}/healthz";
    public const string ReadinessEndpointPath = $"{ApiBasePath}/readiness";
    public const string PrometheusEndpointPath = $"{ApiBasePath}/health/prometheus";

    public const string ReadinessTag = "ready";
    public const string LivenessTag = "live";
    public const string DatabaseTag = "database";
    public const string MessagingTag = "messaging";
}
