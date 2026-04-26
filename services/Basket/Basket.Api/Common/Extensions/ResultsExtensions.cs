using System.Diagnostics;
using FastEndpoints;
using FluentResults;
using FluentValidation.Results;
using Platform.SharedKernel.Errors;

namespace Basket.Api.Common.Extensions;

internal static class ResultsExtensions
{
    /// <summary>
    /// Maps domain error codes raised by the Basket aggregate (all currently typed as
    /// <see cref="ValidationError"/>) onto the HTTP status codes prescribed by
    /// <c>docs/bc-design/error-taxonomy.md § 3.1</c> and <c>use-cases.md § 2.1</c>.
    /// Codes not present here fall back to <see cref="ValidationError"/> → 400.
    /// </summary>
    private static readonly Dictionary<string, int> BasketErrorStatusCodes =
        new(StringComparer.Ordinal)
        {
            ["Basket.Empty"] = StatusCodes.Status409Conflict,
            ["Basket.MaxItemsReached"] = StatusCodes.Status409Conflict,
            ["Basket.InvalidQuantity"] = StatusCodes.Status422UnprocessableEntity,
            ["Basket.CurrencyMismatch"] = StatusCodes.Status422UnprocessableEntity,
            ["Basket.CatalogUnavailable"] = StatusCodes.Status503ServiceUnavailable,
            ["Basket.ProductNotFound"] = StatusCodes.Status404NotFound,
            ["Basket.ItemNotFound"] = StatusCodes.Status404NotFound,
        };

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
    /// Sends an RFC 9457 problem-details response based on the failed <paramref name="result"/>.
    /// HTTP status precedence: forbidden &gt; conflict &gt; not-found &gt; basket-specific
    /// error-code map &gt; default 400. The Basket-specific map keeps all errors typed as
    /// <see cref="ValidationError"/> in the domain layer (M4) while still honouring the
    /// HTTP semantics laid out in <c>use-cases.md § 2.1</c>.
    /// </summary>
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
        var basketStatusOverride = (int?)null;

        foreach (var error in result.Errors)
        {
            switch (error)
            {
                case ValidationError ve:
                    failures.Add(new ValidationFailure(ve.PropertyName, ve.Message)
                    {
                        ErrorCode = ve.ErrorCode,
                    });
                    if (ve.ErrorCode is { Length: > 0 } code &&
                        BasketErrorStatusCodes.TryGetValue(code, out var basketStatus))
                    {
                        basketStatusOverride = Math.Max(basketStatusOverride ?? 0, basketStatus);
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
            await ep.HttpContext.Response.SendErrorsAsync(
                failures,
                StatusCodes.Status500InternalServerError,
                cancellation: ct);
            return;
        }

        var statusCode = StatusCodes.Status400BadRequest;
        if (hasForbidden)
        {
            statusCode = StatusCodes.Status403Forbidden;
        }
        else if (hasConflict)
        {
            statusCode = StatusCodes.Status409Conflict;
        }
        else if (hasNotFound)
        {
            statusCode = StatusCodes.Status404NotFound;
        }
        else if (basketStatusOverride is { } overrideStatus)
        {
            statusCode = overrideStatus;
        }

        await ep.HttpContext.Response.SendErrorsAsync(failures, statusCode, cancellation: ct);
    }
}
