using System.ComponentModel.DataAnnotations;

namespace EShop.BFF.Infrastructure.Common.Config;

/// <summary>
/// Configuration options for BFF readiness-probe timeouts. <see cref="RedisTimeout"/> bounds the
/// <c>redis-cache</c> probe, the only BFF readiness check that leaves the process — the upstream
/// BCs are deliberately not probed.
/// The probe elapses at roughly three times this value — the connect and the ping are bounded
/// separately — so the ceiling is what keeps it inside the deployment's own probe timeout, and
/// raising one means raising the other (<c>docker-compose.yaml</c>'s
/// <c>x-readiness-healthcheck</c> anchor).
/// </summary>
public sealed class HealthChecksOptions
{
    public const string Section = "HealthChecks";

    [Range(typeof(TimeSpan), "00:00:01", "00:00:01")]
    public required TimeSpan RedisTimeout { get; set; }
}
