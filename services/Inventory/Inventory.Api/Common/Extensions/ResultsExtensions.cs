using System.Diagnostics;
using FastEndpoints;
using FluentResults;
using FluentValidation.Results;
using Inventory.Domain.StockItems.Errors;
using Platform.SharedKernel.Errors;

namespace Inventory.Api.Common.Extensions;

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
    /// Sends an RFC 9457 problem-details response based on the failed
    /// <paramref name="result"/>. HTTP status precedence:
    /// forbidden > conflict > not-found > 400.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Inventory's typed business errors (<see cref="InsufficientStockError"/>,
    /// <see cref="ReservationNotActiveError"/>, <see cref="ConcurrencyError"/>)
    /// implement <see cref="IError"/> directly (they are sealed records, not
    /// <see cref="DomainError"/> subclasses) — match each explicitly so they
    /// don't fall through to the catch-all 500 branch. All three map to 409:
    /// the caller raced against state that no longer permits the requested
    /// transition.
    /// </para>
    /// <para>
    /// <see cref="NotFoundError"/> from <c>Platform.SharedKernel</c> covers the
    /// read-side 404s (<c>StockItemNotFound</c>, <c>ReservationNotFound</c>) —
    /// see <c>InventoryErrors</c>.
    /// </para>
    /// </remarks>
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

        // Case-order invariant: NotFound/Conflict/Forbidden/Validation all
        // extend DomainError, so the catch-all DomainError case MUST stay
        // last. The Inventory IError records (InsufficientStockError, etc.)
        // are sealed siblings — their relative order doesn't matter, but
        // they must precede the platform-error cases or the wrong status
        // code wins. Don't reorder alphabetically.
        foreach (var error in result.Errors)
        {
            switch (error)
            {
                case InsufficientStockError ise:
                    hasConflict = true;
                    failures.Add(new ValidationFailure("Quantity", ise.Message)
                    {
                        ErrorCode = "Inventory.InsufficientStock",
                    });
                    continue;
                case ReservationNotActiveError rna:
                    hasConflict = true;
                    failures.Add(new ValidationFailure("ReservationId", rna.Message)
                    {
                        ErrorCode = "Inventory.ReservationNotActive",
                    });
                    continue;
                case ConcurrencyError ce:
                    hasConflict = true;
                    failures.Add(new ValidationFailure("Version", ce.Message)
                    {
                        ErrorCode = "Inventory.Concurrency",
                    });
                    continue;
                case ValidationError ve:
                    failures.Add(new ValidationFailure(ve.PropertyName, ve.Message)
                    {
                        ErrorCode = ve.ErrorCode,
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

        if (failures.Count == 0)
        {
            // No recognised error category — surface a 500 rather than swallow
            // the failure with a misleading 400. Matches Basket's precedent.
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

        await ep.HttpContext.Response.SendErrorsAsync(failures, statusCode, cancellation: ct);
    }
}
