using EShop.BFF.Api.Composition;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using OpenFeature;
using OpenFeature.Model;
using Serilog;

namespace EShop.BFF.IntegrationTests.Common;

internal static class BffTestHostExtensions
{
    /// <summary>
    /// Replaces the platform's static-logger Serilog registration with a DI-only one
    /// (<c>preserveStaticLogger: true</c>). Several BFF fixtures boot a host each in one process, and the
    /// platform's <c>Host.UseSerilog</c> freezes the static <c>Log.Logger</c> on the first build — a second
    /// host then throws "The logger is already frozen". This overrides that registration per host so the
    /// frozen static logger is never rebuilt (same pattern as Catalog's functional fixture).
    /// </summary>
    public static IWebHostBuilder UseTestSerilog(this IWebHostBuilder webBuilder) =>
        webBuilder.ConfigureServices((context, services) =>
            services.AddSerilog(
                (_, loggerConfiguration) => loggerConfiguration
                    .MinimumLevel.Warning()
                    .ReadFrom.Configuration(context.Configuration),
                preserveStaticLogger: true,
                writeToProviders: true));

    /// <summary>
    /// Pins the <c>bff.home-page-eager-cache-warm</c> flag for the <see cref="HomePageCacheWarmer"/> by
    /// substituting <see cref="IFeatureClient"/>. OpenFeature's <c>Api.Instance</c> provider is process-global,
    /// so several fixtures sharing it would contaminate each other's flag reads — substituting the client per
    /// host makes each fixture's warm decision deterministic (the same pattern Catalog's fixture uses).
    /// </summary>
    public static IWebHostBuilder UseWarmFlag(this IWebHostBuilder webBuilder, bool enabled) =>
        webBuilder.ConfigureTestServices(services =>
        {
            var featureClient = Substitute.For<IFeatureClient>();
            featureClient
                .GetBooleanValueAsync(
                    BffFeatureFlags.HomePageEagerCacheWarm,
                    Arg.Any<bool>(),
                    Arg.Any<EvaluationContext>(),
                    Arg.Any<FlagEvaluationOptions>(),
                    Arg.Any<CancellationToken>())
                .Returns(enabled);

            services.AddScoped(_ => featureClient);
        });
}
