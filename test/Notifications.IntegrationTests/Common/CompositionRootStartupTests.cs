using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Notifications.Application.Common;
using Notifications.Infrastructure.Common;
using Platform.Test.Framework;

namespace Notifications.IntegrationTests.Common;

/// <summary>
/// Boots the Notifications composition root against the real <c>appsettings.json</c> and
/// forces every <c>AddOptionsWithValidateOnStart(...)</c> chain to execute. Catches the
/// class of bug where an IOptions <c>[Required]</c> property is misnamed or has no
/// corresponding appsettings key — the failure mode that let three binding mismatches
/// ship undetected before the #214 follow-up (b54472a).
/// </summary>
/// <remarks>
/// No infrastructure dependencies: <c>ConnectionStrings:Notifications</c> is stubbed because
/// options validation never opens a connection; KafkaFlow / OpenTelemetry hosted services
/// never start because we resolve <see cref="IStartupValidator"/> and call
/// <see cref="IStartupValidator.Validate"/> directly instead of <c>RunAsync</c>.
/// </remarks>
public sealed class CompositionRootStartupTests
{
    [Fact]
    public void CompositionRoot_PassesAllOptionsValidationOnStart()
    {
        var appsettingsPath = Path.Combine(
            SolutionPaths.GetSolutionRootDirectory(),
            "services", "Notifications", "Notifications.Api", "appsettings.json");

        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration
            .AddJsonFile(appsettingsPath, optional: false, reloadOnChange: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // ValidateOnStart never opens the DB; this stub just satisfies the
                // [Required] check on ConnectionStringsOptions.Notifications without
                // depending on a host-specific connection string.
                ["ConnectionStrings:Notifications"] =
                    "Host=stub;Port=5432;Database=stub;Username=stub;Password=stub",
            });

        builder.Services
            .AddNotificationsApplication()
            .AddInfrastructure(builder.Configuration, isDeployedEnvironment: false);

        var app = builder.Build();

        // Throws OptionsValidationException if any AddOptionsWithValidateOnStart<...>
        // class is misconfigured. Letting the exception propagate gives xUnit a precise
        // stack trace pointing at the failing option.
        var validator = app.Services.GetRequiredService<IStartupValidator>();
        validator.Validate();
    }
}
