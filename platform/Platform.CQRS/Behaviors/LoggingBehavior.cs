using FluentResults;
using Microsoft.Extensions.Logging;
using Platform.CQRS.Observability;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.Exceptions;
using Serilog.Context;

namespace Platform.CQRS.Behaviors;

internal static class LoggingBehavior
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

            _logger.LogCommandHandling(commandName);

            try
            {
                var result = await _innerHandler.HandleAsync(command, ct);

                if (result.IsSuccess)
                {
                    _logger.LogCommandHandled(commandName);
                }
                else
                {
                    LogResultErrors(result, commandName);
                }

                return result;
            }
            catch (CriticalException ex)
            {
                _logger.LogCommandCriticalException(ex,
                    commandName,
                    ex.GetType().Name,
                    ex.ErrorCode,
                    ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogCommandException(ex, commandName);
                throw;
            }
        }

        private void LogResultErrors(Result<TResponse> result, string commandName)
        {
            using (LogContext.PushProperty(LogMessages.PropertyNames.DomainErrors, result.Errors, true))
            using (LogContext.PushProperty(LogMessages.PropertyNames.DomainError, true))
            {
                foreach (var error in result.Errors.OfType<DomainError>())
                {
                    _logger.LogCommandDomainError(commandName,
                        error.GetType().Name,
                        error.ErrorCode,
                        error.Message);
                }
            }
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

            _logger.LogCommandHandling(commandName);

            try
            {
                var result = await _innerHandler.HandleAsync(command, ct);

                if (result.IsSuccess)
                {
                    _logger.LogCommandHandled(commandName);
                }
                else
                {
                    LogResultErrors(result, commandName);
                }

                return result;
            }
            catch (CriticalException ex)
            {
                _logger.LogCommandCriticalException(ex,
                    commandName,
                    ex.GetType().Name,
                    ex.ErrorCode,
                    ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogCommandException(ex, commandName);
                throw;
            }
        }

        private void LogResultErrors(Result result, string commandName)
        {
            using (LogContext.PushProperty(LogMessages.PropertyNames.DomainErrors, result.Errors, true))
            using (LogContext.PushProperty(LogMessages.PropertyNames.DomainError, true))
            {
                foreach (var error in result.Errors.OfType<DomainError>())
                {
                    _logger.LogCommandDomainError(commandName,
                        error.GetType().Name,
                        error.ErrorCode,
                        error.Message);
                }
            }
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

            _logger.LogQueryHandling(queryName);

            try
            {
                var result = await _innerHandler.HandleAsync(query, ct);

                if (result.IsSuccess)
                {
                    _logger.LogQueryHandled(queryName);
                }
                else
                {
                    LogResultErrors(result, queryName);
                }

                return result;
            }
            catch (CriticalException ex)
            {
                _logger.LogQueryCriticalException(ex,
                    queryName,
                    ex.GetType().Name,
                    ex.ErrorCode,
                    ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogQueryException(ex, queryName);
                throw;
            }
        }

        private void LogResultErrors(Result<TResponse> result, string queryName)
        {
            using (LogContext.PushProperty(LogMessages.PropertyNames.DomainErrors, result.Errors, true))
            using (LogContext.PushProperty(LogMessages.PropertyNames.DomainError, true))
            {
                foreach (var error in result.Errors.OfType<DomainError>())
                {
                    _logger.LogQueryDomainError(queryName,
                        error.GetType().Name,
                        error.ErrorCode,
                        error.Message);
                }
            }
        }
    }
}
