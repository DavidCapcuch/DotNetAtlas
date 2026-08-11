using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Platform.ServiceDefaults.Exceptions;

namespace Platform.ServiceDefaults.UnitTests;

/// <summary>
/// Pins the environment gate <b>inside</b>
/// <see cref="WebApplicationExtensions.UsePlatformExceptionHandling"/>: only <c>Development</c>
/// gets the stack-trace developer page. Every other tier — <c>Testing</c> included — falls through
/// to the <see cref="PlatformExceptionHandler"/> that
/// <see cref="Exceptions.ExceptionHandlerStartupFilter"/> already wired, so test hosts exercise the
/// same RFC 9457 contract a deployed cluster serves, and a deployed tier can never render a stack
/// trace. Drives a real in-memory pipeline so the helper's branch is exercised end-to-end: widening
/// the gate back to every non-deployed tier makes the <c>Testing</c> case render HTML and fail.
/// Scope: this covers the helper only — that each host's <c>Program.cs</c> actually calls it is a
/// wiring concern enforced by compilation and review, not by this test.
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
    public async Task UsePlatformExceptionHandling_WhenDevelopment_RendersDeveloperExceptionPage()
    {
        await using var app = await BuildThrowingHostAsync("Development");
        var client = app.GetTestClient();

        // The developer page only renders HTML when the caller asks for it; without this it falls
        // back to IProblemDetailsService and is indistinguishable from the platform handler.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/boom");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
            body.Should().Contain(
                "kaboom",
                "a developer laptop surfaces the exception detail through the developer page");
        }
    }

    [Fact]
    public async Task UsePlatformExceptionHandling_WhenTesting_ServesTheProblemDetailsContract()
    {
        await using var app = await BuildThrowingHostAsync("Testing");
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/boom", TestContext.Current.CancellationToken);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var body = JsonSerializer.Deserialize<JsonElement>(raw);

        // The developer page also emits problem+json when the caller sends no Accept: text/html,
        // so the media type alone proves nothing — these are PlatformExceptionHandler's exact
        // Title/Detail, which the developer page does not produce.
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
            body.GetProperty("title").GetString().Should().Be(
                "Internal Server Error",
                "test hosts must exercise the same error contract a deployed cluster serves");
            body.GetProperty("detail").GetString().Should().Be(
                "kaboom",
                "a non-deployed tier still surfaces the exception message in ProblemDetails.detail");
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
        builder.Services.AddExceptionHandler<PlatformExceptionHandler>();

        var app = builder.Build();

        // ExceptionHandlerStartupFilter prepends this in every real host; stand it in here so the
        // helper is exercised in the pipeline position it actually occupies.
        app.UseExceptionHandler();
        app.UsePlatformExceptionHandling();
        app.MapGet("/boom", ThrowBoom);

        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;

        static string ThrowBoom() => throw new InvalidOperationException("kaboom");
    }
}
