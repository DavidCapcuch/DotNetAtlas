using System.Diagnostics;
using System.Diagnostics.Metrics;
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
/// Metrics and the request <see cref="Activity"/> are completed by hosting <b>after</b> the client
/// has read the response — and after <c>StopAsync</c> — so those two cases await a signal from
/// their listener instead of asserting straight after the request. Each such case also uses its own
/// route, because a <see cref="MeterListener"/>/<see cref="ActivityListener"/> is process-global and
/// would otherwise be satisfied by a concurrently running test class.
/// </remarks>
public class PlatformExceptionHandlerTests
{
    private const string ThrownMessage = "kaboom";
    private const string RedactedDetail = "An error occurred while processing the request.";
    private const string DefaultRoute = "/boom";
    private const string MetricsRoute = "/boom-metrics";
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
    /// .NET 10 suppresses <c>ExceptionHandlerMiddleware</c> diagnostics whenever an
    /// <c>IExceptionHandler</c> returns <c>true</c>, which silently drops the <c>error.type</c>
    /// dimension from <c>http.server.request.duration</c>. This pins the opt-back-in that
    /// <c>AddServiceDefaults</c> configures — without it the tag is absent and this reads null.
    /// </summary>
    [Fact]
    public async Task AddServiceDefaults_WhenExceptionUnhandled_TagsRequestDurationWithErrorType()
    {
        // Arrange
        var observedErrorType = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Microsoft.AspNetCore.Hosting"
                && instrument.Name == "http.server.request.duration")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            string? route = null;
            string? errorType = null;
            foreach (var tag in tags)
            {
                route = tag.Key == "http.route" ? tag.Value as string : route;
                errorType = tag.Key == "error.type" ? tag.Value as string : errorType;
            }

            if (route == MetricsRoute)
            {
                observedErrorType.TrySetResult(errorType);
            }
        });
        meterListener.Start();

        await using var app = await BuildServiceDefaultsHostAsync("Testing", route: MetricsRoute);

        // Act
        await GetBoomBodyAsync(app, MetricsRoute);
        var errorType = await observedErrorType.Task.WaitAsync(
            SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        errorType.Should().Be(typeof(InvalidOperationException).FullName);
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
    /// Recording middleware diagnostics (above) re-enables the middleware's own
    /// <c>UnhandledException</c> log line, which duplicates the handler's. The Serilog override in
    /// <c>SerilogSetup</c> drops just that line — this pins that exactly one record survives.
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
