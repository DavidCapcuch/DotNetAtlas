using System.Diagnostics;
using FastEndpoints;
using FluentResults;
using FluentValidation.Results;
using Platform.SharedKernel.Errors;

namespace Payments.Api.Common.Extensions;

/// <summary>
/// Translates <see cref="FluentResults"/> failures into FastEndpoints
/// problem-details responses. Mirrors the Ordering precedent
/// (<c>services/Ordering/Ordering.Api/Common/Extensions/ResultsExtensions.cs</c>)
/// — Payments errors are returned as <see cref="ValidationError"/> per
/// <c>error-taxonomy.md § 3.5</c>, so the HTTP mapping picks status by
/// <c>errorCode</c> rather than by error type.
/// </summary>
internal static class ResultsExtensions
{
    /// <summary>
    /// Awaitable variant of FluentResults' <c>Match</c> that lets the
    /// success / failure branches return <see cref="Task"/>s.
    /// </summary>
    public static Task MatchAsync<TIn>(
        this Result<TIn> result,
        Func<TIn, Task> onSuccess,
        Func<Result<TIn>, Task> onFailure)
        => result.IsSuccess ? onSuccess(result.Value) : onFailure(result);

    /// <summary>
    /// Non-generic counterpart of <see cref="MatchAsync{TIn}"/>.
    /// </summary>
    public static Task MatchAsync(
        this Result result,
        Func<Task> onSuccess,
        Func<Result, Task> onFailure)
        => result.IsSuccess ? onSuccess() : onFailure(result);

    /// <summary>
    /// Sends a problem-details error response. Picks the HTTP status from the
    /// strongest signal in the failure list, in priority order:
    /// <list type="number">
    /// <item>Payments-specific <c>errorCode</c> mapping (<c>Payments.NotFound</c> → 404)</item>
    /// <item>Type-based mapping (<see cref="ForbiddenError"/> → 403, <see cref="ConflictError"/> → 409, <see cref="NotFoundError"/> → 404)</item>
    /// <item>Default 400 when only generic <see cref="ValidationError"/> / <see cref="DomainError"/>s are present</item>
    /// <item>500 when the failure carries no recognised error type at all (defensive fallback)</item>
    /// </list>
    /// </summary>
    public static async Task SendErrorResponseAsync<TResult>(
        this IResponseSender ep,
        TResult result,
        CancellationToken ct = default)
        where TResult : ResultBase
    {
        ArgumentNullException.ThrowIfNull(ep);
        ArgumentNullException.ThrowIfNull(result);

        var failures = new List<ValidationFailure>();
        var hasNotFound = false;
        var hasConflict = false;
        var hasForbidden = false;

        foreach (var error in result.Errors)
        {
            switch (error)
            {
                case ValidationError ve:
                    failures.Add(new ValidationFailure(ve.PropertyName, ve.Message)
                    {
                        ErrorCode = ve.ErrorCode,
                    });

                    if (ve.ErrorCode == PaymentsErrorCodes.PaymentNotFound)
                    {
                        hasNotFound = true;
                    }

                    continue;

                case NotFoundError nfe:
                    hasNotFound = true;
                    failures.Add(new ValidationFailure(nfe.ErrorCode, nfe.Message));
                    continue;

                case ConflictError ce:
                    hasConflict = true;
                    failures.Add(new ValidationFailure(ce.ErrorCode, ce.Message));
                    continue;

                case ForbiddenError fe:
                    hasForbidden = true;
                    failures.Add(new ValidationFailure(fe.ErrorCode, fe.Message));
                    continue;

                case DomainError de:
                    failures.Add(new ValidationFailure(de.ErrorCode, de.Message));
                    break;
            }
        }

        if (failures.Count == 0)
        {
            // No recognised error type — surface as 500 with a flagged
            // Activity so the unknown failure shape gets noticed in OTel.
            Activity.Current?.SetStatus(ActivityStatusCode.Error);
            failures.Add(new ValidationFailure("internal_error", "An unexpected error occurred"));
            await ep.HttpContext.Response.SendErrorsAsync(failures, statusCode: 500, cancellation: ct);
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
