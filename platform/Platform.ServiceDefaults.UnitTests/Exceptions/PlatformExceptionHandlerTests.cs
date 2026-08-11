using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.ServiceDefaults.Exceptions;
using Serilog.Core;
using Serilog.Events;

namespace Platform.ServiceDefaults.UnitTests.Exceptions;

/// <summary>
/// Pins the response contract, log, and telemetry that <see cref="PlatformExceptionHandler"/>
/// produces for an unhandled exception. The handler is registered explicitly here rather than
/// relying on a host's <c>Program.cs</c>: a test that only calls <c>AddProblemDetails()</c> is
/// satisfied by the framework's default writer and would stay green with the handler deleted.
/// </summary>
/// <remarks>
/// The request <see cref="Activity"/> is completed by hosting <b>after</b> the client has read the
/// response — and after <c>StopAsync</c> — so that case awaits a signal from its listener instead
/// of asserting straight after the request. It also uses its own route and exception message,
/// because an <see cref="ActivityListener"/> is process-global and would otherwise be satisfied by
/// a concurrently running test class.
/// </remarks>
public class PlatformExceptionHandlerTests
{
    private const string ThrownMessage = "kaboom";
    private const string RedactedDetail = "An error occurred while processing the request.";
    private const string DefaultRoute = "/boom";
    private const string ActivityRoute = "/boom-activity";
    private const string InstanceRoute = "/boom-instance";
    private const string ActivityMessage = "kaboom-activity";

    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    [Trait("Category", "security")]
    public async Task TryHandleAsync_WhenDeployed_RedactsExceptionDetail()
    {
        // Arrange
        await using var app = await BuildThrowingHostAsync("Staging");

        // Act
        var body = await GetBoomBodyAsync(app);

        // Assert
        using (new AssertionScope())
        {
            body.GetProperty("detail").GetString().Should().Be(RedactedDetail);
            body.GetRawText().Should().NotContain(
                ThrownMessage,
                "a deployed tier must not leak the exception message");
            body.GetRawText().Should().NotContain(
                nameof(InvalidOperationException),
                "a deployed tier must not leak the exception type");
        }
    }

    [Fact]
    public async Task TryHandleAsync_WhenNotDeployed_SurfacesExceptionMessageAsDetail()
    {
        // Arrange
        await using var app = await BuildThrowingHostAsync("Testing");

        // Act
        var body = await GetBoomBodyAsync(app);

        // Assert
        body.GetProperty("detail").GetString().Should().Be(ThrownMessage);
    }

    [Fact]
    public async Task TryHandleAsync_WhenExceptionUnhandled_WritesRfc9457ProblemJson()
    {
        // Arrange
        await using var app = await BuildThrowingHostAsync("Staging");

        // Act
        using var response = await app.GetTestClient()
            .GetAsync(DefaultRoute, TestContext.Current.CancellationToken);
        var body = await ReadJsonAsync(response);

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
            body.GetProperty("title").GetString().Should().Be("Internal Server Error");
            body.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status500InternalServerError);
            body.GetProperty("type").GetString().Should()
                .Be("https://tools.ietf.org/html/rfc9110#section-15.6.1");
            body.TryGetProperty("traceId", out var traceId).Should().BeTrue();
            traceId.GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task TryHandleAsync_WhenExceptionUnhandled_LogsTheExceptionOnceAtError()
    {
        // Arrange
        var recorder = new LogRecorder();
        await using var app = await BuildThrowingHostAsync("Staging", recorder);

        // Act
        await GetBoomBodyAsync(app);

        // Assert
        using (new AssertionScope())
        {
            recorder.Errors.Should().ContainSingle();
            recorder.Errors[0].Category.Should().Be(typeof(PlatformExceptionHandler).FullName);
            recorder.Errors[0].Exception.Should().BeOfType<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task TryHandleAsync_WhenExceptionUnhandled_MarksRequestActivityFailed()
    {
        // Arrange
        var failedActivity = new TaskCompletionSource<Activity>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            // Matched on the event AddException records, so the SetStatus assertions below stay
            // independent of the match; the message is unique to this test so a concurrently
            // running class cannot satisfy the signal.
            ActivityStopped = activity =>
            {
                if (activity.Events.Any(RecordsTheActivityException))
                {
                    failedActivity.TrySetResult(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(activityListener);

        await using var app = await BuildThrowingHostAsync(
            "Staging", route: ActivityRoute, message: ActivityMessage);

        // Act
        await GetBoomBodyAsync(app, ActivityRoute);
        var activity = await failedActivity.Task.WaitAsync(
            SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            activity.Status.Should().Be(ActivityStatusCode.Error);
            activity.StatusDescription.Should().Be(ActivityMessage);
        }

        static bool RecordsTheActivityException(ActivityEvent activityEvent) =>
            activityEvent.Name == "exception"
            && activityEvent.Tags.Any(tag =>
                tag.Key == "exception.message" && (tag.Value as string) == ActivityMessage);
    }

    /// <summary>
    /// RFC 9457's <c>instance</c> identifies the specific occurrence, and nothing in the framework
    /// sets it — <c>ProblemDetailsDefaults.Apply</c> fills only status, title, and type. This pins
    /// the <c>CustomizeProblemDetails</c> hook <c>AddServiceDefaults</c> installs, which brings the
    /// platform's 500s in line with the <c>instance</c> FastEndpoints already emits on 4xx.
    /// A route distinct from the other cases keeps it from passing on a coincidental value.
    /// </summary>
    [Fact]
    public async Task AddServiceDefaults_WhenExceptionUnhandled_SetsInstanceToRequestPath()
    {
        // Arrange
        await using var app = await BuildServiceDefaultsHostAsync("Testing", route: InstanceRoute);

        // Act
        var body = await GetBoomBodyAsync(app, InstanceRoute);

        // Assert
        body.GetProperty("instance").GetString().Should().Be(InstanceRoute);
    }

    /// <summary>
    /// <c>ExceptionHandlerMiddleware</c> writes its own <c>UnhandledException</c> record unless it
    /// suppresses diagnostics, which on .NET 10 it does by default once an <c>IExceptionHandler</c>
    /// returns <c>true</c>. That default is what keeps the handler's record the only one, so this
    /// fails if anything opts back into those diagnostics without deduplicating the log.
    /// </summary>
    [Fact]
    public async Task AddServiceDefaults_WhenExceptionUnhandled_LogsTheExceptionOnce()
    {
        // Arrange
        var sink = new LogEventRecorder();
        await using var app = await BuildServiceDefaultsHostAsync("Testing", sink);

        // Act
        await GetBoomBodyAsync(app);

        // Assert
        using (new AssertionScope())
        {
            sink.Errors.Should().ContainSingle();
            sink.Errors[0].Exception.Should().BeOfType<InvalidOperationException>();
        }
    }

    private static async Task<WebApplication> BuildThrowingHostAsync(
        string environmentName,
        ILoggerProvider? loggerProvider = null,
        string route = DefaultRoute,
        string message = ThrownMessage)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName,
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<PlatformExceptionHandler>();

        if (loggerProvider is not null)
        {
            builder.Logging.AddProvider(loggerProvider);
        }

        var app = builder.Build();
        app.UseExceptionHandler();

        return await StartWithBoomEndpointAsync(app, route, message);
    }

    private static async Task<WebApplication> BuildServiceDefaultsHostAsync(
        string environmentName,
        ILogEventSink? sink = null,
        string route = DefaultRoute)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName,
        });
        builder.WebHost.UseTestServer();
        builder.AddServiceDefaults(options =>
        {
            options.ServiceName = "ExceptionHandlerTests";
            if (sink is not null)
            {
                options.ConfigureLogger = (configuration, _) => configuration.WriteTo.Sink(sink);
            }
        });

        // AddServiceDefaults registers the handler and prepends UseExceptionHandler via
        // ExceptionHandlerStartupFilter, so the pipeline needs nothing else here.
        return await StartWithBoomEndpointAsync(builder.Build(), route);
    }

    private static async Task<WebApplication> StartWithBoomEndpointAsync(
        WebApplication app,
        string route,
        string message = ThrownMessage)
    {
        app.MapGet(route, string () => throw new InvalidOperationException(message));
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static async Task<JsonElement> GetBoomBodyAsync(
        WebApplication app,
        string route = DefaultRoute)
    {
        using var response = await app.GetTestClient()
            .GetAsync(route, TestContext.Current.CancellationToken);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonSerializer.Deserialize<JsonElement>(raw);
    }

    private sealed record LogRecord(string Category, Exception? Exception);

    private sealed class LogRecorder : ILoggerProvider
    {
        public List<LogRecord> Errors { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CategoryLogger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class CategoryLogger(LogRecorder recorder, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Error && exception is not null)
                {
                    recorder.Errors.Add(new LogRecord(category, exception));
                }
            }
        }
    }

    private sealed class LogEventRecorder : ILogEventSink
    {
        public List<LogEvent> Errors { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            if (logEvent.Level >= LogEventLevel.Error && logEvent.Exception is not null)
            {
                Errors.Add(logEvent);
            }
        }
    }
}
