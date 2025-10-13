using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Domain.Errors.Base;
using FluentResults;
using FluentValidation;

namespace DotNetAtlas.Application.Common.Behaviors;

internal static class ValidationHandlerBehavior
{
    internal sealed class CommandHandler<TCommand, TResponse> : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        private readonly ICommandHandler<TCommand, TResponse> _innerHandler;
        private readonly IEnumerable<IValidator<TCommand>> _validators;

        public CommandHandler(
            ICommandHandler<TCommand, TResponse> innerHandler,
            IEnumerable<IValidator<TCommand>> validators)
        {
            _innerHandler = innerHandler;
            _validators = validators;
        }

        public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken ct)
        {
            var validationFailures = await ValidateAsync(command, _validators);

            if (validationFailures.Length == 0)
            {
                return await _innerHandler.HandleAsync(command, ct);
            }

            return Result.Fail<TResponse>(validationFailures);
        }
    }

    internal sealed class CommandBaseHandler<TCommand> : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        private readonly ICommandHandler<TCommand> _innerHandler;
        private readonly IEnumerable<IValidator<TCommand>> _validators;

        public CommandBaseHandler(
            ICommandHandler<TCommand> innerHandler,
            IEnumerable<IValidator<TCommand>> validators)
        {
            _innerHandler = innerHandler;
            _validators = validators;
        }

        public async Task<Result> HandleAsync(TCommand command, CancellationToken ct)
        {
            var validationFailures = await ValidateAsync(command, _validators);

            if (validationFailures.Length == 0)
            {
                return await _innerHandler.HandleAsync(command, ct);
            }

            return Result.Fail(validationFailures);
        }
    }

    internal sealed class QueryHandler<TQuery, TResponse> : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        private readonly IQueryHandler<TQuery, TResponse> _innerHandler;
        private readonly IEnumerable<IValidator<TQuery>> _validators;

        public QueryHandler(
            IQueryHandler<TQuery, TResponse> innerHandler,
            IEnumerable<IValidator<TQuery>> validators)
        {
            _innerHandler = innerHandler;
            _validators = validators;
        }

        public async Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken ct)
        {
            var validationFailures = await ValidateAsync(query, _validators);

            if (validationFailures.Length == 0)
            {
                return await _innerHandler.HandleAsync(query, ct);
            }

            return Result.Fail(validationFailures);
        }
    }

    private static async Task<ValidationError[]> ValidateAsync<TRequest>(
        TRequest command,
        IEnumerable<IValidator<TRequest>> validators)
    {
        var context = new ValidationContext<TRequest>(command);

        var validationResults = await Task.WhenAll(
            validators.Select(validator => validator.ValidateAsync(context)));

        var validationFailures = validationResults
            .Where(validationResult => !validationResult.IsValid)
            .SelectMany(validationResult => validationResult.Errors)
            .Select(error => new ValidationError(error.PropertyName, error.ErrorMessage, error.ErrorCode))
            .ToArray();

        return validationFailures;
    }
}
