using System.Diagnostics;
using FluentResults;
using Platform.CQS.Observability;
using Platform.SharedKernel.Errors;

namespace Platform.CQS.Behaviors;

/// <summary>
/// Handler behavior that tracks command/query metrics including success, errors, exceptions, and duration.
/// </summary>
internal static class MetricsBehavior
{
    private static IEnumerable<(string ErrorType, string ErrorCode)> ExtractDomainErrors(ResultBase result)
        => result.Errors.OfType<DomainError>().Select(e => (e.GetType().Name, e.ErrorCode));

    internal sealed class CommandHandler<TCommand, TResponse> : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        private readonly ICommandHandler<TCommand, TResponse> _innerHandler;

        public CommandHandler(
            ICommandHandler<TCommand, TResponse> innerHandler)
        {
            _innerHandler = innerHandler;
        }

        public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken ct)
        {
            var commandName = typeof(TCommand).Name;
            var startTimeStamp = Stopwatch.GetTimestamp();

            try
            {
                var result = await _innerHandler.HandleAsync(command, ct);
                var elapsedMs = Stopwatch.GetElapsedTime(startTimeStamp).TotalMilliseconds;

                if (result.IsSuccess)
                {
                    CqsInstrumentation.RecordCommandSuccess(commandName, elapsedMs);
                }
                else
                {
                    CqsInstrumentation.RecordCommandFailure(commandName, elapsedMs, ExtractDomainErrors(result));
                }

                return result;
            }
            catch (Exception ex)
            {
                var elapsedMs = Stopwatch.GetElapsedTime(startTimeStamp).TotalMilliseconds;
                CqsInstrumentation.RecordCommandException(commandName, elapsedMs, ex);
                throw;
            }
        }
    }

    internal sealed class CommandBaseHandler<TCommand> : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        private readonly ICommandHandler<TCommand> _innerHandler;

        public CommandBaseHandler(
            ICommandHandler<TCommand> innerHandler)
        {
            _innerHandler = innerHandler;
        }

        public async Task<Result> HandleAsync(TCommand command, CancellationToken ct)
        {
            var commandName = typeof(TCommand).Name;
            var startTimeStamp = Stopwatch.GetTimestamp();

            try
            {
                var result = await _innerHandler.HandleAsync(command, ct);
                var elapsedMs = Stopwatch.GetElapsedTime(startTimeStamp).TotalMilliseconds;

                if (result.IsSuccess)
                {
                    CqsInstrumentation.RecordCommandSuccess(commandName, elapsedMs);
                }
                else
                {
                    CqsInstrumentation.RecordCommandFailure(commandName, elapsedMs, ExtractDomainErrors(result));
                }

                return result;
            }
            catch (Exception ex)
            {
                var elapsedMs = Stopwatch.GetElapsedTime(startTimeStamp).TotalMilliseconds;
                CqsInstrumentation.RecordCommandException(commandName, elapsedMs, ex);
                throw;
            }
        }
    }

    internal sealed class QueryHandler<TQuery, TResponse> : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        private readonly IQueryHandler<TQuery, TResponse> _innerHandler;

        public QueryHandler(
            IQueryHandler<TQuery, TResponse> innerHandler)
        {
            _innerHandler = innerHandler;
        }

        public async Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken ct)
        {
            var queryName = typeof(TQuery).Name;
            var startTimeStamp = Stopwatch.GetTimestamp();

            try
            {
                var result = await _innerHandler.HandleAsync(query, ct);
                var elapsedMs = Stopwatch.GetElapsedTime(startTimeStamp).TotalMilliseconds;

                if (result.IsSuccess)
                {
                    CqsInstrumentation.RecordQuerySuccess(queryName, elapsedMs);
                }
                else
                {
                    CqsInstrumentation.RecordQueryFailure(queryName, elapsedMs, ExtractDomainErrors(result));
                }

                return result;
            }
            catch (Exception ex)
            {
                var elapsedMs = Stopwatch.GetElapsedTime(startTimeStamp).TotalMilliseconds;
                CqsInstrumentation.RecordQueryException(queryName, elapsedMs, ex);
                throw;
            }
        }
    }
}
