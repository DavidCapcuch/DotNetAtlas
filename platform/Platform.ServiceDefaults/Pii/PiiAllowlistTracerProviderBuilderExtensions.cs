using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace Platform.ServiceDefaults.Pii;

/// <summary>
/// DI + OTel pipeline extensions for the <see cref="PiiAllowlistProcessor"/> (ADR-0011).
/// </summary>
public static class PiiAllowlistTracerProviderBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="PiiAllowlistOptions"/> (bound to configuration section
    /// <c>Observability:PiiAllowlist</c>) and the <see cref="PiiAllowlistProcessor"/> as a singleton.
    /// Callers still need to attach the processor to their tracer pipeline via
    /// <see cref="WithPiiAllowlist"/>.
    /// </summary>
    public static IServiceCollection AddPiiAllowlistProcessor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptionsWithValidateOnStart<PiiAllowlistOptions>()
            .BindConfiguration(PiiAllowlistOptions.Section);
        services.AddSingleton<PiiAllowlistProcessor>();
        return services;
    }

    /// <summary>
    /// Attaches the <see cref="PiiAllowlistProcessor"/> to the OTel tracer pipeline.
    /// Pair with <see cref="AddPiiAllowlistProcessor"/> at DI-registration time.
    /// </summary>
    public static TracerProviderBuilder WithPiiAllowlist(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddProcessor(sp => sp.GetRequiredService<PiiAllowlistProcessor>());
    }
}
