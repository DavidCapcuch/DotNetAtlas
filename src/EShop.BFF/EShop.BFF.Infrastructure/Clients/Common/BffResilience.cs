using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace EShop.BFF.Infrastructure.Clients.Common;

/// <summary>
/// The BFF's single outbound resilience-pipeline shape (bff.md § 2.1). The platform ships no
/// per-service presets — cross-service resilience is YARP's job at the edge — but a per-service Polly
/// pipeline belongs wherever the component owns its own client, and the BFF owns its typed clients.
/// </summary>
/// <remarks>
/// Applied per client under a distinct name so circuit-breaker state is isolated — a Catalog outage
/// must not break Inventory calls (bff.md § 2.1). Strategy order (outer → inner):
/// total-timeout (15 s wall) → retry (×2, exponential backoff + jitter) → circuit-breaker →
/// per-attempt timeout (2 s).
/// </remarks>
internal static class BffResilience
{
    private static readonly TimeSpan TotalRequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(2);

    public static IHttpClientBuilder AddBffResilience(this IHttpClientBuilder builder, string clientName)
    {
        builder.AddResilienceHandler($"bff-{clientName}", pipeline =>
        {
            // Outermost: a hard wall across all attempts so a retry storm can't amplify a latency spike.
            pipeline.AddTimeout(TotalRequestTimeout);

            // Retry transient failures (5xx / 408 / 429 / HttpRequestException / per-attempt timeout —
            // the HttpRetryStrategyOptions default predicate). 4xx other than 408/429 are not retried.
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(100),
            });

            // Per-client circuit breaker — opens on sustained failure, isolating one upstream's outage
            // from the others (bff.md § 2.1: "open after 5 consecutive failures within a 10s window").
            // Polly v8 is ratio-based, not consecutive-count-based, so a high FailureRatio over a
            // 5-call minimum window is the faithful expression of "5 (near-)consecutive failures":
            // a lone 4xx amid healthy traffic must not contribute to opening.
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 0.9,
                MinimumThroughput = 5,
                SamplingDuration = TimeSpan.FromSeconds(10),
                BreakDuration = TimeSpan.FromSeconds(30),
            });

            // Innermost: bound each individual attempt.
            pipeline.AddTimeout(PerAttemptTimeout);
        });

        return builder;
    }
}
