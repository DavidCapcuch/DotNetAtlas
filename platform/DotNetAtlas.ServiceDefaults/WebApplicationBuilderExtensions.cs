using System.Reflection;
using DotNetAtlas.ServiceDefaults.Config;
using DotNetAtlas.ServiceDefaults.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DotNetAtlas.ServiceDefaults;

/// <summary>
/// Extension methods for WebApplicationBuilder to configure service defaults.
/// </summary>
public static class WebApplicationBuilderExtensions
{
    /// <summary>
    /// Configures all service defaults including host configuration and Serilog logging.
    /// This is a convenience method that calls <see cref="AddPlatformHostConfiguration"/> and <see cref="UsePlatformSerilog"/>.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="configureOptions">Optional callback to configure Serilog options.</param>
    /// <returns>The web application builder for chaining.</returns>
    public static WebApplicationBuilder AddServiceDefaults(
        this WebApplicationBuilder builder,
        Action<SerilogOptions>? configureOptions = null)
    {
        builder.AddPlatformHostConfiguration();
        builder.UsePlatformSerilog(configureOptions);

        return builder;
    }

    /// <summary>
    /// Configures platform host defaults including environment variables, user secrets (Local environment only),
    /// and service provider validation (non-cluster environments only).
    /// </summary>
    /// <remarks>
    /// <para>This method configures:</para>
    /// <list type="bullet">
    ///   <item><description>Environment variables added to configuration</description></item>
    ///   <item><description>User secrets loaded from entry assembly (Local environment only)</description></item>
    ///   <item><description>Service provider validation with ValidateScopes and ValidateOnBuild (non-cluster environments only)</description></item>
    /// </list>
    /// </remarks>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The web application builder for chaining.</returns>
    public static WebApplicationBuilder AddPlatformHostConfiguration(this WebApplicationBuilder builder)
    {
        builder.Configuration.AddEnvironmentVariables();

        if (builder.Environment.IsLocal())
        {
            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly is not null)
            {
                builder.Configuration.AddUserSecrets(entryAssembly, optional: true);
            }
        }

        var isClusterEnvironment = builder.Environment.IsDeployedEnvironment();
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = !isClusterEnvironment;
            options.ValidateOnBuild = !isClusterEnvironment;
        });

        return builder;
    }

    /// <summary>
    /// Configures Serilog logging with platform defaults including environment-specific sinks and enrichers.
    /// </summary>
    /// <remarks>
    /// <para>This method configures Serilog with:</para>
    /// <list type="bullet">
    ///   <item><description>ECS HTTP context enricher</description></item>
    ///   <item><description>Log context enricher</description></item>
    ///   <item><description>Exception details enricher (including EF Core)</description></item>
    ///   <item><description>Console sink (plain in cluster, formatted with tracing in non-cluster)</description></item>
    ///   <item><description>Seq sink (non-cluster environments only)</description></item>
    ///   <item><description>OpenTelemetry sink (when OTEL_EXPORTER_OTLP_ENDPOINT is configured)</description></item>
    /// </list>
    /// </remarks>
    /// <param name="builder">The web application builder.</param>
    /// <param name="configureOptions">Optional callback to configure Serilog options.</param>
    /// <returns>The web application builder for chaining.</returns>
    public static WebApplicationBuilder UsePlatformSerilog(
        this WebApplicationBuilder builder,
        Action<SerilogOptions>? configureOptions = null)
    {
        var isClusterEnvironment = builder.Environment.IsDeployedEnvironment();

        var serilogOptions = new SerilogOptions();
        configureOptions?.Invoke(serilogOptions);
        builder.UseSerilogInternal(isClusterEnvironment, serilogOptions);

        return builder;
    }
}
