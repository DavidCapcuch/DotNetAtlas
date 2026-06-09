using Microsoft.Extensions.Logging;

namespace Notifications.IntegrationTests.Common;

/// <summary>
/// Captures formatted log messages so tests can assert on log output — the fake SMS channel's
/// only transport (notifications.md § 6) and the handler's quiet-hours deferral line.
/// </summary>
internal sealed class CollectingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
    }
}
