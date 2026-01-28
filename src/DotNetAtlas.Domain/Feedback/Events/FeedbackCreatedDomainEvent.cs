using DotNetAtlas.Domain.Feedback.ValueObjects;
using DotNetAtlas.SharedKernel.Base.DomainEvents;

namespace DotNetAtlas.Domain.Feedback.Events;

/// <summary>
/// Domain event raised when new feedback is created.
/// Contains the initial state of the feedback.
/// </summary>
public sealed record FeedbackCreatedDomainEvent : DomainEvent
{
    /// <summary>
    /// Identifier of the feedback aggregate that was created.
    /// </summary>
    public required Guid FeedbackId { get; init; }

    /// <summary>
    /// User who created the feedback.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Initial feedback text.
    /// </summary>
    public required FeedbackText Text { get; init; }

    /// <summary>
    /// Initial rating (1-5).
    /// </summary>
    public required FeedbackRating Rating { get; init; }
}
