using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Platform.ServiceDefaults.UnitTests;

/// <summary>
/// Pins the environment gate <b>inside</b>
/// <see cref="WebApplicationExtensions.UsePlatformExceptionHandling"/>: a deployed tier must fail
/// closed — the redacted platform handler, never the stack-trace developer page — while developer
/// tiers (Development/Testing) keep the full diagnostics. Drives a real in-memory pipeline so the
/// helper's branch is exercised end-to-end, not just its predicate: reverting the helper's gate to
/// <c>IsProduction()</c> makes a deployed-but-non-Production tier render the developer page and fails
/// the deployed case here. Scope: this covers the helper only — that each host's <c>Program.cs</c>
/// actually calls it is a wiring concern enforced by compilation and review, not by this test.
/// </summary>
public class UsePlatformExceptionHandlingTests
{
    [Fact]
    [Trait("Category", "security")]
    public async Task UsePlatformExceptionHandling_WhenDeployed_DoesNotLeakExceptionDetail()
    {
        await using var app = await BuildThrowingHostAsync("Staging");
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/boom", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            body.Should().NotContain("kaboom", "a deployed tier must not leak the exception message");
            body.Should().NotContain(
                nameof(InvalidOperationException),
                "a deployed tier must not leak the exception type or stack trace");
        }
    }

    [Fact]
    public async Task UsePlatformExceptionHandling_WhenTesting_RendersDeveloperExceptionPage()
    {
        await using var app = await BuildThrowingHostAsync("Testing");
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/boom", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            body.Should().Contain(
                "kaboom",
                "developer tiers surface the exception detail through the developer exception page");
        }
    }

    private static async Task<WebApplication> BuildThrowingHostAsync(string environmentName)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName,
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();

        var app = builder.Build();
        app.UsePlatformExceptionHandling();
        app.MapGet("/boom", ThrowBoom);

        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;

        static string ThrowBoom() => throw new InvalidOperationException("kaboom");
    }
}
