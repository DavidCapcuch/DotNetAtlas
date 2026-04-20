using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Platform.ServiceDefaults.Resilience;

/// <summary>
/// HTTP-client resilience presets tuned to the ADR-0009 single-AZ reference profile
/// (≤ 50 rps sustained, p99 ≤ 500 ms end-to-end). Each preset layers on top of
/// <c>Microsoft.Extensions.Http.Resilience</c>'s standard pipeline
/// (total-timeout → retry → circuit-breaker → attempt-timeout) and tunes the knobs for
/// a specific call-site class.
/// </summary>
/// <remarks>
/// Naming matches the Wave 0 platform contract:
/// <list type="bullet">
/// <item><description><c>read-idempotent</c> — safe to retry hard, no breaker</description></item>
/// <item><description><c>write-command</c> — retry once, breaker trips on sustained failure</description></item>
/// <item><description><c>batch-read</c> — long-running safe reads, one retry, no breaker</description></item>
/// </list>
/// All presets accept an optional caller-supplied <see cref="Action{HttpStandardResilienceOptions}"/>
/// that is applied last and can override any preset value (e.g., tests use this to shrink delays).
/// </remarks>
public static class ResiliencePresets
{
    /// <summary>
    /// Aggressive retry for idempotent reads — three retries with jittered exponential backoff and
    /// no circuit breaker. Use for <c>GET</c>s that the caller can safely repeat.
    /// </summary>
    public static IHttpStandardResiliencePipelineBuilder AddReadIdempotentResiliencePreset(
        this IHttpClientBuilder builder,
        Action<HttpStandardResilienceOptions>? configure = null)
    {
        return builder.AddStandardResilienceHandler(options =>
        {
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);

            options.Retry.MaxRetryAttempts = 3;
            options.Retry.BackoffType = DelayBackoffType.Exponential;
            options.Retry.UseJitter = true;
            options.Retry.Delay = TimeSpan.FromMilliseconds(500);

            // Disable the circuit breaker: reads are safe to retry and a breaker would reduce
            // availability for independent callers. 1.0 + huge throughput effectively never trips.
            options.CircuitBreaker.FailureRatio = 1.0;
            options.CircuitBreaker.MinimumThroughput = int.MaxValue / 2;
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(5);

            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Careful retry for write commands — one retry then circuit-break on sustained failure.
    /// Use for <c>POST</c>/<c>PUT</c>/<c>DELETE</c> to downstream services; combine with an
    /// idempotency key on the receiver per ADR-0013.
    /// </summary>
    public static IHttpStandardResiliencePipelineBuilder AddWriteCommandResiliencePreset(
        this IHttpClientBuilder builder,
        Action<HttpStandardResilienceOptions>? configure = null)
    {
        return builder.AddStandardResilienceHandler(options =>
        {
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);

            options.Retry.MaxRetryAttempts = 1;
            options.Retry.BackoffType = DelayBackoffType.Constant;
            options.Retry.UseJitter = true;
            options.Retry.Delay = TimeSpan.FromMilliseconds(500);

            options.CircuitBreaker.FailureRatio = 0.5;
            options.CircuitBreaker.MinimumThroughput = 10;
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);

            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Long-running batch read — 2 minute total budget, 60 s per attempt, one retry, no breaker.
    /// Use for bulk GETs, background exports, large joins.
    /// </summary>
    public static IHttpStandardResiliencePipelineBuilder AddBatchReadResiliencePreset(
        this IHttpClientBuilder builder,
        Action<HttpStandardResilienceOptions>? configure = null)
    {
        return builder.AddStandardResilienceHandler(options =>
        {
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(2);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);

            options.Retry.MaxRetryAttempts = 1;
            options.Retry.BackoffType = DelayBackoffType.Exponential;
            options.Retry.UseJitter = true;
            options.Retry.Delay = TimeSpan.FromSeconds(1);

            // Disabled for the same reasons as read-idempotent.
            options.CircuitBreaker.FailureRatio = 1.0;
            options.CircuitBreaker.MinimumThroughput = int.MaxValue / 2;
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(5);

            configure?.Invoke(options);
        });
    }
}
