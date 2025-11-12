using DotNetAtlas.Domain.Common.Events;

namespace DotNetAtlas.Domain.Entities.Weather.Feedback.Events;

/// <summary>
/// Domain event raised when feedback is updated (text and/or rating).
/// Contains complete before/after state - compare old vs new to determine what changed.
/// </summary>
public sealed record FeedbackChangedDomainEvent : IDomainEvent
{
    /// <summary>
    /// Identifier of the feedback aggregate that was changed.
    /// </summary>
    public required Guid FeedbackId { get; init; }

    /// <summary>
    /// User who made the change.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Previous feedback text. Compare with NewText to detect if text changed.
    /// </summary>
    public required string OldText { get; init; }

    /// <summary>
    /// Current feedback text. If equal to OldText, text did not change.
    /// </summary>
    public required string NewText { get; init; }

    /// <summary>
    /// Previous rating (1-5). Compare with NewRating to detect if rating changed.
    /// </summary>
    public required int OldRating { get; init; }

    /// <summary>
    /// Current rating (1-5). If equal to OldRating, rating did not change.
    /// </summary>
    public required int NewRating { get; init; }

    /// <summary>
    /// UTC timestamp when event occurred.
    /// </summary>
    public required DateTimeOffset OccurredOnUtc { get; init; }
}
