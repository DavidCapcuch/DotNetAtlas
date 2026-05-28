using System.Diagnostics;
using FastEndpoints;
using FluentResults;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Platform.SharedKernel.Errors;

namespace Platform.Api.Extensions;

/// <summary>
/// Extension methods on <see cref="IResponseSender"/> that turn a failed
/// <see cref="ResultBase"/> into an RFC 9457 ProblemDetails response.
/// </summary>
/// <remarks>
/// <para>
/// Dispatch is pure <see cref="DomainError"/> type-switch — there are no per-BC
/// overrides and no DI registration. Each typed <see cref="DomainError"/> subclass
/// has a canonical HTTP status:
/// </para>
/// <list type="table">
/// <listheader><term>Error type</term><description>HTTP status</description></listheader>
/// <item><term><see cref="ServiceUnavailableError"/></term><description>503 Service Unavailable</description></item>
/// <item><term><see cref="NotImplementedError"/></term><description>501 Not Implemented</description></item>
/// <item><term><see cref="ForbiddenError"/></term><description>403 Forbidden</description></item>
/// <item><term><see cref="ConflictError"/></term><description>409 Conflict</description></item>
/// <item><term><see cref="NotFoundError"/></term><description>404 Not Found</description></item>
/// <item><term><see cref="ValidationError"/></term><description>422 Unprocessable Entity (RFC 9457: well-formed but semantically invalid; 400 belongs to pre-handler input-shape validators)</description></item>
/// <item><term>Unknown <see cref="DomainError"/> subclass</term><description>400 Bad Request</description></item>
/// <item><term>Unrecognised <see cref="IError"/></term><description>500 Internal Server Error</description></item>
/// </list>
/// <para>
/// When a <see cref="Result"/> carries multiple errors with different statuses, the
/// most-severe status wins (precedence top-down in the table above). BCs that want a
/// status not covered by the canonical types should add a new <see cref="DomainError"/>
/// subclass in <c>Platform.SharedKernel.Errors</c> rather than overriding here.
/// </para>
/// </remarks>
public static class ResponseSenderExtensions
{
    /// <summary>
    /// Sends an RFC 9457 ProblemDetails response derived from a failed
    /// <paramref name="result"/>. Must only be called when
    /// <see cref="ResultBase.IsFailed"/> is <c>true</c>; otherwise the response
    /// will be a misleading 5xx.
    /// </summary>
    public static async Task SendErrorResponseAsync<TResult>(
        this IResponseSender ep,
        TResult result,
        CancellationToken ct = default)
        where TResult : ResultBase
    {
        var (failures, statusCode) = MapToProblem(result);
        await ep.HttpContext.Response.SendErrorsAsync(failures, statusCode, cancellation: ct);
    }

    /// <summary>
    /// Pure mapping from <see cref="ResultBase.Errors"/> to a
    /// <see cref="ValidationFailure"/> list + HTTP status. Extracted so the dispatch
    /// logic can be unit-tested without an <see cref="HttpContext"/>.
    /// </summary>
    internal static (List<ValidationFailure> Failures, int StatusCode) MapToProblem(ResultBase result)
    {
        var failures = new List<ValidationFailure>();
        var hasServiceUnavailable = false;
        var hasNotImplemented = false;
        var hasForbidden = false;
        var hasConflict = false;
        var hasNotFound = false;
        var hasValidation = false;
        var hasUnknownDomainError = false;

        foreach (var error in result.Errors)
        {
            switch (error)
            {
                case ServiceUnavailableError sue:
                    hasServiceUnavailable = true;
                    failures.Add(new ValidationFailure(sue.ResourceName, sue.Message)
                    {
                        ErrorCode = sue.ErrorCode,
                    });
                    continue;
                case NotImplementedError nie:
                    hasNotImplemented = true;
                    failures.Add(new ValidationFailure(nie.FeatureName, nie.Message)
                    {
                        ErrorCode = nie.ErrorCode,
                    });
                    continue;
                case ForbiddenError fe:
                    hasForbidden = true;
                    failures.Add(new ValidationFailure(fe.EntityName, fe.Message)
                    {
                        ErrorCode = fe.ErrorCode,
                    });
                    continue;
                case ConflictError ce:
                    hasConflict = true;
                    failures.Add(new ValidationFailure(ce.EntityName, ce.Message)
                    {
                        ErrorCode = ce.ErrorCode,
                    });
                    continue;
                case NotFoundError nfe:
                    hasNotFound = true;
                    failures.Add(new ValidationFailure(nfe.EntityName, nfe.Message)
                    {
                        ErrorCode = nfe.ErrorCode,
                    });
                    continue;
                case ValidationError ve:
                    hasValidation = true;
                    failures.Add(new ValidationFailure(ve.PropertyName, ve.Message)
                    {
                        ErrorCode = ve.ErrorCode,
                    });
                    continue;
                case DomainError de:
                    hasUnknownDomainError = true;
                    failures.Add(new ValidationFailure(de.ErrorCode, de.Message)
                    {
                        ErrorCode = de.ErrorCode,
                    });
                    continue;
            }
        }

        // No recognised domain-error category — surface 500 rather than swallow with a misleading 400.
        if (failures.Count == 0)
        {
            Activity.Current?.SetStatus(ActivityStatusCode.Error);
            failures.Add(new ValidationFailure("internal_error", "An unexpected error occurred"));
            return (failures, StatusCodes.Status500InternalServerError);
        }

        // Precedence: most-severe wins. 503 > 501 > 403 > 409 > 404 > 422 > 400.
        var statusCode = hasUnknownDomainError
            ? StatusCodes.Status400BadRequest
            : StatusCodes.Status400BadRequest;
        if (hasValidation)
        {
            statusCode = StatusCodes.Status422UnprocessableEntity;
        }

        if (hasNotFound)
        {
            statusCode = StatusCodes.Status404NotFound;
        }

        if (hasConflict)
        {
            statusCode = StatusCodes.Status409Conflict;
        }

        if (hasForbidden)
        {
            statusCode = StatusCodes.Status403Forbidden;
        }

        if (hasNotImplemented)
        {
            statusCode = StatusCodes.Status501NotImplemented;
        }

        if (hasServiceUnavailable)
        {
            statusCode = StatusCodes.Status503ServiceUnavailable;
        }

        return (failures, statusCode);
    }
}
