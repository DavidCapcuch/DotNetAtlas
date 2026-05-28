using FluentResults;

namespace Platform.Api.Extensions;

/// <summary>
/// FluentResults convenience helpers used by FastEndpoints handlers to branch
/// between success and failure paths without juggling <see cref="ResultBase.IsSuccess"/>
/// and <see cref="ResultBase.IsFailed"/> at every call site. Pure helpers — no per-BC
/// state.
/// </summary>
public static class ResultMatchExtensions
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
}
