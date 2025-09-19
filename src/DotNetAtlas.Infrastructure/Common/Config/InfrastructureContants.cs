namespace DotNetAtlas.Infrastructure.Common.Config;

public static class InfrastructureContants
{
    public const string HealthEndpointPath = "/api/healthz";
    public const string ReadinessEndpointPath = "/api/readiness";
    public const string PrometheusEndpointPath = "/api/health/prometheus";
    public const string ReadinessTag = "ready";
    public const string LivenessTag = "live";
    public const string DatabaseTag = "database";
    public const string ApiTag = "api";
}
