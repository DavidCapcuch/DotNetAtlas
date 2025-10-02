using System.Diagnostics;
using System.Text.Json;
using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Domain.Errors.Base;
using FluentResults;

namespace DotNetAtlas.Application.Common.Behaviors;

internal static class TracingHandlerBehavior
{
    internal sealed class CommandHandler<TCommand, TResponse> : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        private readonly ICommandHandler<TCommand, TResponse> _innerHandler;
        private readonly IDotNetAtlasInstrumentation _instrumentation;

        public CommandHandler(
            ICommandHandler<TCommand, TResponse> innerHandler,
            IDotNetAtlasInstrumentation instrumentation)
        {
            _innerHandler = innerHandler;
            _instrumentation = instrumentation;
        }

        public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken ct)
        {
            var commandName = typeof(TCommand).Name;

            using var activity = _instrumentation.StartActivity(commandName);

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
    }

    internal sealed class CommandBaseHandler<TCommand> : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        private readonly ICommandHandler<TCommand> _innerHandler;
        private readonly IDotNetAtlasInstrumentation _instrumentation;

        public CommandBaseHandler(
            ICommandHandler<TCommand> innerHandler,
            IDotNetAtlasInstrumentation instrumentation)
        {
            _innerHandler = innerHandler;
            _instrumentation = instrumentation;
        }

        public async Task<Result> HandleAsync(TCommand command, CancellationToken ct)
        {
            var commandName = typeof(TCommand).Name;

            using var activity = _instrumentation.StartActivity(commandName);

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
    }

    internal sealed class QueryHandler<TQuery, TResponse> : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        private readonly IQueryHandler<TQuery, TResponse> _innerHandler;
        private readonly IDotNetAtlasInstrumentation _instrumentation;

        public QueryHandler(
            IQueryHandler<TQuery, TResponse> innerHandler,
            IDotNetAtlasInstrumentation instrumentation)
        {
            _innerHandler = innerHandler;
            _instrumentation = instrumentation;
        }

        public async Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken ct)
        {
            var queryName = typeof(TQuery).Name;

            using var activity = _instrumentation.StartActivity(queryName);

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
    }

    private static void TraceResultFailure(Activity? activity, ResultBase result)
    {
        activity?.SetTag(DiagnosticNames.DomainError, true);
        activity?.SetTag(DiagnosticNames.DomainErrorCount, result.Errors.Count);

        if (activity?.IsAllDataRequested == true)
        {
            var detailsJson = BuildErrorDetailsJson(result);

            activity?.AddEvent(new ActivityEvent(
                "Domain error",
                tags: new ActivityTagsCollection
                {
                    [DiagnosticNames.DomainErrorCount] = result.Errors.Count,
                    [DiagnosticNames.DomainErrorDetails] = detailsJson
                }));
        }
    }

    private static string BuildErrorDetailsJson(ResultBase result)
    {
        var errorDetails = result.Errors
            .Select(err => new
            {
                code = err is DomainError de ? de.ErrorCode : null,
                message = err.Message
            });

        return JsonSerializer.Serialize(errorDetails);
    }
}
