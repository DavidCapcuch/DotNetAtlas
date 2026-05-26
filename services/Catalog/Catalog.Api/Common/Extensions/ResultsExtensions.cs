using System.Diagnostics;
using FastEndpoints;
using FluentResults;
using FluentValidation.Results;
using Platform.SharedKernel.Errors;

namespace Catalog.Api.Common.Extensions;

internal static class ResultsExtensions
{
    /// <summary>
    /// Maps Catalog-specific <see cref="DomainError.ErrorCode"/> values onto the HTTP status
    /// codes prescribed by <c>docs/bc-design/error-taxonomy.md § 3.2 + § 4</c>.
    /// Codes not present here fall back to <see cref="ValidationError"/> → 400.
    /// </summary>
    /// <remarks>
    /// <c>Product.NotFound</c> / <c>Category.NotFound</c> / <c>Category.ParentNotFound</c> are
    /// surfaced through the broader <see cref="NotFoundError"/> precedence rule (404) rather
    /// than the validation-error map; they appear here for documentation only because Catalog's
    /// domain currently models them as <see cref="ValidationError"/> instances. The numeric
    /// override below promotes them to 404 even when authored as <see cref="ValidationError"/>.
    /// </remarks>
    private static readonly Dictionary<string, int> CatalogErrorStatusCodes =
        new(StringComparer.Ordinal)
        {
            // 404 — entity addressing miss
            ["Product.NotFound"] = StatusCodes.Status404NotFound,
            ["Category.NotFound"] = StatusCodes.Status404NotFound,
            ["Category.ParentNotFound"] = StatusCodes.Status404NotFound,

            // 409 — invariant precondition (state-transition) conflict
            ["Product.SkuAlreadyExists"] = StatusCodes.Status409Conflict,
            ["Product.CannotRepriceDiscontinued"] = StatusCodes.Status409Conflict,
            ["Product.CannotModifyDiscontinued"] = StatusCodes.Status409Conflict,

            // 422 — validation / business-rule failures (default for ValidationError, listed for clarity)
            ["Product.CategoryIdRequired"] = StatusCodes.Status422UnprocessableEntity,
            ["Product.ReasonRequired"] = StatusCodes.Status422UnprocessableEntity,
            ["Category.NameRequired"] = StatusCodes.Status422UnprocessableEntity,
            ["Category.NameTooLong"] = StatusCodes.Status422UnprocessableEntity,
            ["Category.MaxDepthExceeded"] = StatusCodes.Status422UnprocessableEntity,
            ["Category.CannotParentToSelf"] = StatusCodes.Status422UnprocessableEntity,
            ["Category.ReparentCreatesCycle"] = StatusCodes.Status422UnprocessableEntity,

            // 403 — policy violation (admin-only flag missing on reactivate)
            ["Product.ReactivationRequiresAdminFlag"] = StatusCodes.Status403Forbidden,
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
    /// Sends an RFC 9457 problem-details response derived from a failed <paramref name="result"/>.
    /// Status precedence: forbidden &gt; conflict &gt; not-found &gt; Catalog-error-code map &gt;
    /// default 400. The map promotes specific <see cref="DomainError.ErrorCode"/> values to
    /// 404 / 409 / 422 / 403 per <c>error-taxonomy.md § 4</c>.
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
        int? catalogStatusOverride = null;

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
                        CatalogErrorStatusCodes.TryGetValue(code, out var catalogStatus))
                    {
                        catalogStatusOverride = Math.Max(catalogStatusOverride ?? 0, catalogStatus);
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
        else if (catalogStatusOverride is { } overrideStatus)
        {
            statusCode = overrideStatus;
        }

        await ep.HttpContext.Response.SendErrorsAsync(failures, statusCode, cancellation: ct);
    }
}
