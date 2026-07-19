using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenFeature;
using OpenFeature.Hooks;
using OpenFeature.Hosting.Providers.Memory;

namespace Platform.ServiceDefaults.FeatureFlags;

/// <summary>
/// DI extensions for OpenFeature + JSON-file provider wiring (ADR-0014).
/// </summary>
public static class FeatureFlagsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="FeatureFlagsOptions"/> (bound to configuration section
    /// <c>FeatureFlags</c>), the OpenFeature <see cref="TraceEnricherHook"/> (official OTel
    /// semantic-convention enrichment), and an in-memory OpenFeature provider hydrated from the
    /// JSON flag file at <see cref="FeatureFlagsOptions.FilePath"/>.
    /// </summary>
    /// <remarks>
    /// Opt-in — not wired into <c>AddServiceDefaults()</c>. Consuming BCs call this explicitly from
    /// their <c>Program.cs</c> after <c>AddServiceDefaults()</c>. Production adopters replace
    /// <c>AddInMemoryProvider</c> with a SaaS provider (LaunchDarkly / Split / ConfigCat) — call
    /// sites using <see cref="IFeatureClient"/> remain unchanged.
    /// </remarks>
    public static IServiceCollection AddFeatureFlags(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptionsWithValidateOnStart<FeatureFlagsOptions>()
            .BindConfiguration(FeatureFlagsOptions.Section);

        services.AddOpenFeature(builder =>
        {
            builder.AddInMemoryProvider(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<FeatureFlagsOptions>>().Value;
                var logger = sp.GetService<ILoggerFactory>()?.CreateLogger(typeof(JsonFlagLoader));
                return JsonFlagLoader.Load(opts.FilePath, logger);
            });

            builder.AddHook(new TraceEnricherHook());
        });

        return services;
    }
}
