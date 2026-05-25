using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Inventory.IntegrationTests.Common;

/// <summary>
/// <see cref="ISaveChangesInterceptor"/> that runs an async callback
/// immediately before the first <c>SaveChangesAsync</c> call on a DbContext.
/// Used by concurrency tests to inject a competing row between the
/// repository's rehydrate and save steps — deterministic without real
/// threading.
/// </summary>
internal sealed class OneShotConflictInterceptor : ISaveChangesInterceptor
{
    private readonly Func<CancellationToken, Task> _injectConflict;
    private readonly int _firesRemaining;
    private int _firedCount;

    /// <summary>
    /// OneShotConflictInterceptor.
    /// </summary>
    /// <param name="injectConflict">
    /// The work to run before save. Typically inserts a competing row via a
    /// fresh DbContext/scope.
    /// </param>
    /// <param name="fireCount">
    /// How many saves to intercept. Default 1 (the first attempt). Pass 2 to
    /// also trip the retry, forcing the repository to exhaust its one-retry
    /// budget and surface <c>ConcurrencyError</c>.
    /// </param>
    public OneShotConflictInterceptor(Func<CancellationToken, Task> injectConflict, int fireCount = 1)
    {
        _injectConflict = injectConflict;
        _firesRemaining = fireCount;
    }

    public async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_firedCount < _firesRemaining)
        {
            _firedCount++;
            await _injectConflict(cancellationToken);
        }

        return result;
    }
}
