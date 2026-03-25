using Platform.SharedKernel.Base.DomainEvents;
using Weather.Domain.Feedback.ValueObjects;

namespace Weather.Domain.Feedback.Events;

/// <summary>
/// Domain event raised when feedback is updated (text and/or rating).
/// Contains complete before/after state - compare old vs new to determine what changed.
/// </summary>
public sealed record FeedbackChangedDomainEvent : DomainEvent
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
    public required FeedbackText OldText { get; init; }

    /// <summary>
    /// Current feedback text. If equal to OldText, text did not change.
    /// </summary>
    public required FeedbackText NewText { get; init; }

    /// <summary>
    /// Previous rating (1-5). Compare with NewRating to detect if rating changed.
    /// </summary>
    public required FeedbackRating OldRating { get; init; }

    /// <summary>
    /// Current rating (1-5). If equal to OldRating, rating did not change.
    /// </summary>
    public required FeedbackRating NewRating { get; init; }
}
