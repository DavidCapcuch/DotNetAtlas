using System.Diagnostics;
using FluentResults;
using Platform.CQS.Observability;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.Exceptions;

namespace Platform.CQS.Behaviors;

internal static class TracingBehavior
{
    internal sealed class CommandHandler<TCommand, TResponse> : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        private readonly ICommandHandler<TCommand, TResponse> _innerHandler;

        public CommandHandler(ICommandHandler<TCommand, TResponse> innerHandler)
        {
            _innerHandler = innerHandler;
        }

        public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken ct)
        {
            var commandName = typeof(TCommand).Name;

            using var activity = CqsInstrumentation.ActivitySource.StartActivity(commandName);

            try
            {
                var result = await _innerHandler.HandleAsync(command, ct);

                if (result.IsSuccess)
                {
                    activity?.SetStatus(ActivityStatusCode.Ok);
                }
                else
                {
                    TraceResultFailure(activity, result);
                }

                return result;
            }
            catch (CriticalException ex)
            {
                TraceCriticalException(activity, ex);
                throw;
            }
            catch (Exception ex)
            {
                TraceException(activity, ex);
                throw;
            }
        }
    }

    internal sealed class CommandBaseHandler<TCommand> : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        private readonly ICommandHandler<TCommand> _innerHandler;

        public CommandBaseHandler(ICommandHandler<TCommand> innerHandler)
        {
            _innerHandler = innerHandler;
        }

        public async Task<Result> HandleAsync(TCommand command, CancellationToken ct)
        {
            var commandName = typeof(TCommand).Name;

            using var activity = CqsInstrumentation.ActivitySource.StartActivity(commandName);

            try
            {
                var result = await _innerHandler.HandleAsync(command, ct);

                if (result.IsSuccess)
                {
                    activity?.SetStatus(ActivityStatusCode.Ok);
                }
                else
                {
                    TraceResultFailure(activity, result);
                }

                return result;
            }
            catch (CriticalException ex)
            {
                TraceCriticalException(activity, ex);
                throw;
            }
            catch (Exception ex)
            {
                TraceException(activity, ex);
                throw;
            }
        }
    }

    internal sealed class QueryHandler<TQuery, TResponse> : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        private readonly IQueryHandler<TQuery, TResponse> _innerHandler;

        public QueryHandler(IQueryHandler<TQuery, TResponse> innerHandler)
        {
            _innerHandler = innerHandler;
        }

        public async Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken ct)
        {
            var queryName = typeof(TQuery).Name;

            using var activity = CqsInstrumentation.ActivitySource.StartActivity(queryName);

            try
            {
                var result = await _innerHandler.HandleAsync(query, ct);

                if (result.IsSuccess)
                {
                    activity?.SetStatus(ActivityStatusCode.Ok);
                }
                else
                {
                    TraceResultFailure(activity, result);
                }

                return result;
            }
            catch (CriticalException ex)
            {
                TraceCriticalException(activity, ex);
                throw;
            }
            catch (Exception ex)
            {
                TraceException(activity, ex);
                throw;
            }
        }
    }

    private static void TraceResultFailure(Activity? activity, ResultBase result)
    {
        if (activity?.IsAllDataRequested != true)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error);
        activity.SetTag(TraceTags.DomainError, true);
        activity.SetTag(TraceTags.DomainErrorCount, result.Errors.Count);

        foreach (var error in result.Errors)
        {
            if (error is DomainError domainError)
            {
                activity.AddEvent(new ActivityEvent(TraceTags.DomainErrorEvent, tags: new ActivityTagsCollection
                {
                    {
                        TraceTags.ErrorType, domainError.GetType().Name
                    },
                    {
                        TraceTags.ErrorCode, domainError.ErrorCode
                    },
                    {
                        TraceTags.ErrorMessage, domainError.Message
                    }
                }));
            }
            else
            {
                activity.AddEvent(new ActivityEvent(TraceTags.ErrorEvent, tags: new ActivityTagsCollection
                {
                    {
                        TraceTags.ErrorType, error.GetType().Name
                    },
                    {
                        TraceTags.ErrorMessage, error.Message
                    }
                }));
            }
        }
    }

    private static void TraceCriticalException(Activity? activity, CriticalException ex)
    {
        activity?.SetTag(TraceTags.ExceptionCritical, true);
        activity?.SetTag(TraceTags.ExceptionCode, ex.ErrorCode);
        TraceException(activity, ex);
    }

    private static void TraceException(Activity? activity, Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.AddException(ex);
    }
}
