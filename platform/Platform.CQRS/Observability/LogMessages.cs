using Microsoft.Extensions.Logging;

namespace Platform.CQRS.Observability;

/// <summary>
/// Since CQRS handlers are potential hot paths, we use source-generated logging.
/// </summary>
/// <remarks>
/// See https://learn.microsoft.com/en-us/dotnet/core/extensions/logging-library-authors.
/// </remarks>
internal static partial class LogMessages
{
    /// <summary>
    /// Property names for Serilog LogContext.
    /// </summary>
    internal static class PropertyNames
    {
        internal const string DomainErrors = "DomainErrors";
        internal const string DomainError = "DomainError";
    }

    [LoggerMessage(
        EventId = LogEventIds.Command.Handling,
        Level = LogLevel.Information,
        Message = "Handling command {CommandName}")]
    internal static partial void LogCommandHandling(this ILogger logger, string commandName);

    [LoggerMessage(
        EventId = LogEventIds.Command.Handled,
        Level = LogLevel.Information,
        Message = "Command {CommandName} handled successfully")]
    internal static partial void LogCommandHandled(this ILogger logger, string commandName);

    [LoggerMessage(
        EventId = LogEventIds.Command.DomainError,
        Level = LogLevel.Warning,
        Message = "Command {CommandName} returned domain error [{ErrorType}] {ErrorCode}: {ErrorMessage}")]
    internal static partial void LogCommandDomainError(
        this ILogger logger,
        string commandName,
        string errorType,
        string errorCode,
        string errorMessage);

    [LoggerMessage(
        EventId = LogEventIds.Command.Exception,
        Level = LogLevel.Error,
        Message = "Command {CommandName} threw an exception")]
    internal static partial void LogCommandException(
        this ILogger logger,
        Exception exception,
        string commandName);

    [LoggerMessage(
        EventId = LogEventIds.Command.CriticalException,
        Level = LogLevel.Critical,
        Message = "Command {CommandName} threw critical exception {ExceptionType} [{ErrorCode}]: {ErrorMessage}")]
    internal static partial void LogCommandCriticalException(
        this ILogger logger,
        Exception exception,
        string commandName,
        string exceptionType,
        string errorCode,
        string errorMessage);

    [LoggerMessage(
        EventId = LogEventIds.Query.Handling,
        Level = LogLevel.Information,
        Message = "Handling query {QueryName}")]
    internal static partial void LogQueryHandling(this ILogger logger, string queryName);

    [LoggerMessage(
        EventId = LogEventIds.Query.Handled,
        Level = LogLevel.Information,
        Message = "Query {QueryName} handled successfully")]
    internal static partial void LogQueryHandled(this ILogger logger, string queryName);

    [LoggerMessage(
        EventId = LogEventIds.Query.DomainError,
        Level = LogLevel.Warning,
        Message = "Query {QueryName} returned domain error [{ErrorType}] {ErrorCode}: {ErrorMessage}")]
    internal static partial void LogQueryDomainError(
        this ILogger logger,
        string queryName,
        string errorType,
        string errorCode,
        string errorMessage);

    [LoggerMessage(
        EventId = LogEventIds.Query.Exception,
        Level = LogLevel.Error,
        Message = "Query {QueryName} threw an exception")]
    internal static partial void LogQueryException(
        this ILogger logger,
        Exception exception,
        string queryName);

    [LoggerMessage(
        EventId = LogEventIds.Query.CriticalException,
        Level = LogLevel.Critical,
        Message = "Query {QueryName} threw critical exception {ExceptionType} [{ErrorCode}]: {ErrorMessage}")]
    internal static partial void LogQueryCriticalException(
        this ILogger logger,
        Exception exception,
        string queryName,
        string exceptionType,
        string errorCode,
        string errorMessage);
}
