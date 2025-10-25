using System.Diagnostics;
using DotNetAtlas.Domain.Common.Errors;
using FastEndpoints;
using FluentResults;
using FluentValidation.Results;

namespace DotNetAtlas.Api.Common.Extensions;

internal static class ResultsExtensions
{
    public static Task MatchAsync<TIn>(
        this Result<TIn> result,
        Func<TIn, Task> onSuccess,
        Func<Result<TIn>, Task> onFailure)
    {
        return result.IsSuccess ? onSuccess(result.Value) : onFailure(result);
    }

    public static Task MatchAsync(
        this Result result,
        Func<Task> onSuccess,
        Func<Result, Task> onFailure)
    {
        return result.IsSuccess ? onSuccess() : onFailure(result);
    }

    /// <summary>
    /// Sends an error response based on the provided result object, mapping specific error types to appropriate HTTP status codes.
    /// </summary>
    /// <typeparam name="TResult">The type of the result object, which must inherit from <see cref="ResultBase"/>.</typeparam>
    /// <param name="ep">The response sender used to send the response to the client.</param>
    /// <param name="result">The result object containing error details.</param>
    /// <param name="ct"><see cref="CancellationToken"/>.</param>
    public static async Task SendErrorResponseAsync<TResult>(
        this IResponseSender ep,
        TResult result,
        CancellationToken ct = default)
        where TResult : ResultBase
    {
        var failures = new List<ValidationFailure>();
        var hasConflict = false;
        var hasNotFound = false;
        var hasForbidden = false;

        foreach (var error in result.Errors)
        {
            switch (error)
            {
                case ValidationError ve:
                    failures.Add(new ValidationFailure(ve.PropertyName, ve.Message)
                    {
                        ErrorCode = ve.ErrorCode
                    });
                    continue;
                case NotFoundError nfe:
                    hasNotFound = true;
                    failures.Add(new ValidationFailure(nfe.ErrorCode, nfe.Message));
                    continue;
                case ConflictError ce:
                    hasConflict = true;
                    failures.Add(new ValidationFailure(ce.ErrorCode, ce.Message));
                    continue;
                case ForbiddenError ue:
                    hasForbidden = true;
                    failures.Add(new ValidationFailure(ue.ErrorCode, ue.Message));
                    continue;
                case DomainError de:
                    failures.Add(new ValidationFailure(de.ErrorCode, de.Message));
                    break;
            }
        }

        var hasDomainError = failures.Count > 0;
        if (!hasDomainError)
        {
            Activity.Current?.SetStatus(ActivityStatusCode.Error);
            failures.Add(new ValidationFailure("internal_error", "An unexpected error occurred"));
            await ep.HttpContext.Response.SendErrorsAsync(failures, 500, cancellation: ct);
            return;
        }

        var statusCode = 400;
        if (hasForbidden)
        {
            statusCode = 403;
        }
        else if (hasConflict)
        {
            statusCode = 409;
        }
        else if (hasNotFound)
        {
            statusCode = 404;
        }

        await ep.HttpContext.Response.SendErrorsAsync(failures, statusCode, cancellation: ct);
    }
}
