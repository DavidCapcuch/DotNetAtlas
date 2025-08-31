using DotNetAtlas.Application.Common.CQS;
using FluentResults;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace DotNetAtlas.Application.Common.Behaviors;

internal static class LoggingHandlerBehavior
{
    internal sealed class CommandHandler<TCommand, TResponse> : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        private readonly ICommandHandler<TCommand, TResponse> _innerHandler;
        private readonly ILogger<CommandHandler<TCommand, TResponse>> _logger;

        public CommandHandler(
            ICommandHandler<TCommand, TResponse> innerHandler,
            ILogger<CommandHandler<TCommand, TResponse>> logger)
        {
            _innerHandler = innerHandler;
            _logger = logger;
        }

        public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken ct)
        {
            var commandName = typeof(TCommand).Name;

            _logger.LogInformation("Processing command {Command}", commandName);

            var result = await _innerHandler.HandleAsync(command, ct);

            if (result.IsSuccess)
            {
                _logger.LogInformation("Completed command {Command}", commandName);
            }
            else
            {
                using (LogContext.PushProperty("DomainErrors", result.Errors, true))
                using (LogContext.PushProperty("DomainError", true))
                {
                    _logger.LogWarning(
                        "Completed command {Command} with error; Error count: {ErrorCount}",
                        commandName,
                        result.Errors.Count);
                }
            }

            return result;
        }
    }

    internal sealed class CommandBaseHandler<TCommand> : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        private readonly ICommandHandler<TCommand> _innerHandler;
        private readonly ILogger<CommandBaseHandler<TCommand>> _logger;

        public CommandBaseHandler(
            ICommandHandler<TCommand> innerHandler,
            ILogger<CommandBaseHandler<TCommand>> logger)
        {
            _innerHandler = innerHandler;
            _logger = logger;
        }

        public async Task<Result> HandleAsync(TCommand command, CancellationToken ct)
        {
            var commandName = typeof(TCommand).Name;

            _logger.LogInformation("Processing command {Command}", commandName);

            var result = await _innerHandler.HandleAsync(command, ct);

            if (result.IsSuccess)
            {
                _logger.LogInformation("Completed command {Command}", commandName);
            }
            else
            {
                using (LogContext.PushProperty("DomainErrors", result.Errors, true))
                using (LogContext.PushProperty("DomainError", true))
                {
                    _logger.LogWarning(
                        "Completed command {Command} with error; Error count: {ErrorCount}",
                        commandName,
                        result.Errors.Count);
                }
            }

            return result;
        }
    }

    internal sealed class QueryHandler<TQuery, TResponse> : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        private readonly IQueryHandler<TQuery, TResponse> _innerHandler;
        private readonly ILogger<QueryHandler<TQuery, TResponse>> _logger;

        public QueryHandler(
            IQueryHandler<TQuery, TResponse> innerHandler,
            ILogger<QueryHandler<TQuery, TResponse>> logger)
        {
            _innerHandler = innerHandler;
            _logger = logger;
        }

        public async Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken ct)
        {
            var queryName = typeof(TQuery).Name;

            _logger.LogInformation("Processing query {Query}", queryName);

            var result = await _innerHandler.HandleAsync(query, ct);

            if (result.IsSuccess)
            {
                _logger.LogInformation("Completed query {Query}", queryName);
            }
            else
            {
                using (LogContext.PushProperty("DomainErrors", result.Errors, true))
                using (LogContext.PushProperty("DomainError", true))
                {
                    _logger.LogWarning(
                        "Completed query {Query} with error; Error count: {ErrorCount}",
                        queryName,
                        result.Errors.Count);
                }
            }

            return result;
        }
    }
}
