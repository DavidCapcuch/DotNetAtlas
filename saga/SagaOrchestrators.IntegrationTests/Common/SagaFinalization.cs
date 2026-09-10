namespace SagaOrchestrators.IntegrationTests.Common;

/// <summary>
/// The outcome of waiting for a saga to be finalized, carrying everything needed to explain a
/// failure — the wait itself does not throw, so this is the only record of what was seen.
/// </summary>
/// <param name="IsFinalized">Whether the row went away before the timeout.</param>
/// <param name="SagaType">The saga state type, named in the failure message.</param>
/// <param name="CorrelationId">The saga instance the wait was watching.</param>
/// <param name="Timeout">How long the wait was given.</param>
/// <param name="LastObservedState">
/// Where the witness saw the saga before the wait began. Not necessarily where it was when the
/// wait started — only that it existed.
/// </param>
/// <param name="CurrentState">Where the saga is now, read fresh; <c>gone</c> when no row remains.</param>
public sealed record SagaFinalization(
    bool IsFinalized,
    string SagaType,
    Guid CorrelationId,
    TimeSpan Timeout,
    string LastObservedState,
    string CurrentState);
