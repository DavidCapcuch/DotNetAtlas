using System.Diagnostics;
using DotNetAtlas.Application.Common.Observability.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace DotNetAtlas.Test.Framework.Tracing;

/// <summary>
/// Manages OpenTelemetry activity lifecycle for test execution, providing distributed tracing
/// for test runs with automatic activity creation, tagging, failure tracking, and cleanup.
/// Inspired by https://github.com/martinjt/unittest-with-otel/tree/main.
/// </summary>
public sealed class TestCaseTracer : IDisposable
{
    private readonly Activity? _testActivity;
    private readonly TracerProvider _tracerProvider;
    private readonly ILogger<TestCaseTracer> _logger;
    private readonly string? _traceId;

    /// <summary>
    /// Initializes a new test activity with appropriate tags for a test trace.
    /// </summary>
    /// <param name="serviceProvider">Service provider to resolve tracing dependencies.</param>
    /// <param name="testMethodName">Name of the test method being executed.</param>
    /// <param name="testCaseId">Unique identifier for the test case.</param>
    /// <param name="testType">Type of test (e.g., "functional", "integration").</param>
    public TestCaseTracer(
        IServiceProvider serviceProvider,
        string testMethodName,
        string testCaseId,
        string testType)
    {
        _logger = serviceProvider.GetRequiredService<ILogger<TestCaseTracer>>();
        _testActivity = DotNetAtlasActivitySource.StartActivity(testMethodName);
        _testActivity?.SetTag("is.test.trace", true);
        _testActivity?.SetTag("test.case.id", testCaseId);
        _testActivity?.SetTag("test.type", testType);

        _tracerProvider = serviceProvider.GetRequiredService<TracerProvider>();

        // Log test start with Jaeger trace URL
        if (_testActivity?.TraceId != null)
        {
            _traceId = _testActivity.TraceId.ToString();
            _logger.LogInformation("Test {TestMethod} started, trace: http://localhost:16686/jaeger/ui/trace/{TraceId}",
                testMethodName, _traceId);
        }
        else
        {
            _logger.LogInformation("Test {TestMethod} started (no trace available)", testMethodName);
        }
    }

    /// <summary>
    /// Gets the trace ID for tracing context propagation (e.g., via traceparent header).
    /// </summary>
    public string? TraceParent => _testActivity?.Id;

    /// <summary>
    /// Records a test failure by adding exception information and marking the activity as failed.
    /// </summary>
    /// <remarks>
    /// Follows OpenTelemetry test attributes conventions
    /// https://opentelemetry.io/docs/specs/semconv/registry/attributes/test/.
    /// </remarks>
    /// <param name="exceptionMessages">Collection of exception messages from the failed test.</param>
    public void RecordTestFailure(IEnumerable<string>? exceptionMessages)
    {
        _testActivity?.AddException(
            new Exception(string.Join(';', exceptionMessages ?? [])));
        _testActivity?.SetStatus(ActivityStatusCode.Error);
        _testActivity?.SetTag("test.case.result.status", "fail");
    }

    public void LogTestTraceLocalJaegerLink()
    {
        _logger.LogInformation("Test finished, trace: http://localhost:16686/jaeger/ui/trace/{TraceId}", _traceId);
    }

    /// <summary>
    /// Flushes telemetry and disposes the test activity.
    /// </summary>
    public void Dispose()
    {
        _tracerProvider.ForceFlush();
        _testActivity?.Dispose();
    }
}
