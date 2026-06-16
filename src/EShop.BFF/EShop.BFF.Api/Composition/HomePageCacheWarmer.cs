using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenFeature;

namespace EShop.BFF.Api.Composition;

/// <summary>
/// Pre-warms the <c>home-page:v1</c> cache shortly after startup so the most-hit endpoint never serves a
/// cold cache miss to its first visitor (bff.md § 3.4, ADR-0014). Feature-flag gated by
/// <see cref="BffFeatureFlags.HomePageEagerCacheWarm"/> (default ON): flipping it OFF skips the warm
/// cleanly, leaving no half-baked state. Runs as a <see cref="BackgroundService"/> — the warm happens
/// <em>off</em> the host-startup path, so a slow-but-not-failing upstream delays only the warm, never the
/// host becoming ready. The warm is best-effort: any failure is logged and swallowed (the first request
/// composes on demand).
/// </summary>
internal sealed class HomePageCacheWarmer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HomePageCacheWarmer> _logger;

    public HomePageCacheWarmer(
        IServiceScopeFactory scopeFactory,
        ILogger<HomePageCacheWarmer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // IFeatureClient and HomePageProvider are scoped (they compose with the request-scoped typed
            // clients), so resolve both in a dedicated scope rather than capturing them in this singleton.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var featureClient = scope.ServiceProvider.GetRequiredService<IFeatureClient>();

            var enabled = await featureClient.GetBooleanValueAsync(
                BffFeatureFlags.HomePageEagerCacheWarm,
                defaultValue: true,
                cancellationToken: stoppingToken);

            if (!enabled)
            {
                _logger.LogInformation(
                    "Home-page eager cache warm is disabled ({Flag} = off); skipping startup warm",
                    BffFeatureFlags.HomePageEagerCacheWarm);
                return;
            }

            var homePage = scope.ServiceProvider.GetRequiredService<HomePageProvider>();
            await homePage.GetOrComposeAsync(stoppingToken);

            _logger.LogInformation("Home-page cache warmed on startup");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort: a cold-start upstream outage / flag-provider hiccup must not surface — the BFF
            // degrades and the first request composes on demand (bff.md § 3.4).
            _logger.LogWarning(ex, "Home-page eager cache warm failed; the first request will compose on demand");
        }
    }
}
