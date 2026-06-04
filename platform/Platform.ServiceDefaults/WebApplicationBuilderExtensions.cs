using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Platform.ServiceDefaults.Config;
using Platform.ServiceDefaults.Exceptions;
using Platform.ServiceDefaults.Logging;

namespace Platform.ServiceDefaults;

/// <summary>
/// Extension methods for WebApplicationBuilder to configure service defaults.
/// </summary>
public static class WebApplicationBuilderExtensions
{
    /// <summary>
    /// Configures all service defaults including host configuration and Serilog logging. Time
    /// access uses the BCL <see cref="TimeProvider"/> (registered by the Generic Host) per
    /// ADR-0015.
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

        // Catch-all exception handler — auto-wired via IStartupFilter so BCs don't
        // need `app.UseExceptionHandler()` in Program.cs. AddProblemDetails is
        // idempotent — BCs may also call it; the second call is a no-op.
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<PlatformExceptionHandler>();
        builder.Services.AddTransient<IStartupFilter, ExceptionHandlerStartupFilter>();

        return builder;
    }

    /// <summary>
    /// Configures platform host defaults including environment variables, user secrets (Development environment only),
    /// and service provider validation (non-cluster environments only).
    /// </summary>
    /// <remarks>
    /// <para>This method configures:</para>
    /// <list type="bullet">
    ///   <item><description>Environment variables added to configuration</description></item>
    ///   <item><description>User secrets loaded from entry assembly (Development environment only)</description></item>
    ///   <item><description>Service provider validation with ValidateScopes and ValidateOnBuild (non-cluster environments only)</description></item>
    /// </list>
    /// </remarks>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The web application builder for chaining.</returns>
    internal static WebApplicationBuilder AddPlatformHostConfiguration(this WebApplicationBuilder builder)
    {
        builder.Configuration.AddEnvironmentVariables();

        if (builder.Environment.IsDevelopment())
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

        AssertAllowedHostsPinnedInDeployedEnvironment(builder);

        return builder;
    }

    /// <summary>
    /// Fails closed when a deployed environment has not pinned <c>AllowedHosts</c> to a
    /// non-wildcard, non-empty value. Mirrors the <c>ValidAudience</c> contract in
    /// <see cref="Auth.JwtBearerConfigurator"/>: the base <c>appsettings.json</c> files in
    /// this repo deliberately do NOT carry an <c>AllowedHosts</c> key, so each deployed
    /// environment must pin a real host list via <c>ASPNETCORE_ALLOWEDHOSTS</c> or an
    /// <c>appsettings.&lt;Env&gt;.json</c> overlay.
    /// </summary>
    /// <remarks>
    /// <c>Microsoft.AspNetCore.HostFiltering</c> defaults to "accept all" when the key is
    /// missing or set to <c>"*"</c> — silently permissive. Catching the misconfiguration at
    /// builder time produces a loud startup failure instead of a security hole.
    /// </remarks>
    private static void AssertAllowedHostsPinnedInDeployedEnvironment(WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDeployedEnvironment())
        {
            return;
        }

        var allowedHosts = builder.Configuration["AllowedHosts"];
        if (string.IsNullOrWhiteSpace(allowedHosts) || allowedHosts == "*")
        {
            throw new InvalidOperationException(
                "AllowedHosts must be pinned to a non-wildcard, non-empty value in deployed environments. " +
                "Set ASPNETCORE_ALLOWEDHOSTS=<host1>;<host2> or pin under an " +
                "appsettings.<Env>.json overlay. Wildcard '*' and null/empty are rejected — " +
                "they leave Host-header validation silently permissive.");
        }
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
    internal static WebApplicationBuilder UsePlatformSerilog(
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
