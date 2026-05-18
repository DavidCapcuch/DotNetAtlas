using FluentResults;
using Platform.SharedKernel.Base;
using Weather.Domain.Feedback.Errors;
using Weather.Domain.Feedback.Events;
using Weather.Domain.Feedback.ValueObjects;

namespace Weather.Domain.Feedback;

/// <summary>
/// Aggregate root representing user feedback with text and rating.
/// Tracks creation and modification with auditable timestamps.
/// </summary>
/// <remarks>
/// This aggregate can raise the following domain events:
/// <list type="bullet">
/// <item><see cref="FeedbackCreatedDomainEvent"/>: When new feedback is created.</item>
/// <item><see cref="FeedbackChangedDomainEvent"/>: When feedback text or rating is modified.</item>
/// </list>
/// </remarks>
public sealed class Feedback : AggregateRoot<Guid>, IAuditableEntity
{
    public FeedbackText FeedbackText { get; private set; } = null!;

    public FeedbackRating Rating { get; private set; } = null!;

    public Guid CreatedByUser { get; private set; }

    private Feedback()
    {
    }

    /// <summary>
    /// Factory method to create new feedback.
    /// </summary>
    /// <param name="feedbackText">The feedback text content.</param>
    /// <param name="rating">The feedback rating.</param>
    /// <param name="createdByUser">The user ID of the feedback creator.</param>
    /// <param name="utcNow">Current UTC time stamped on the raised domain event (ADR-0015).</param>
    /// <returns>A result containing the new feedback instance.</returns>
    /// <remarks>
    /// Possible raised events:
    /// <list type="bullet">
    /// <item><see cref="FeedbackCreatedDomainEvent"/>: Always raised when new feedback is created.</item>
    /// </list>
    /// </remarks>
    public static Result<Feedback> Create(
        FeedbackText feedbackText,
        FeedbackRating rating,
        Guid createdByUser,
        DateTimeOffset utcNow)
    {
        var feedback = new Feedback
        {
            Id = Guid.CreateVersion7(),
            FeedbackText = feedbackText,
            Rating = rating,
            CreatedByUser = createdByUser
        };

        feedback.AddDomainEvent(
            new FeedbackCreatedDomainEvent
            {
                OccurredOnUtc = utcNow,
                FeedbackId = feedback.Id,
                UserId = feedback.CreatedByUser,
                Rating = rating,
                Text = feedbackText
            });

        return feedback;
    }

    /// <summary>
    /// Changes the feedback text and/or rating.
    /// </summary>
    /// <param name="feedback">The new feedback text.</param>
    /// <param name="rating">The new rating.</param>
    /// <param name="byUserId">The user ID attempting the change (must be the creator).</param>
    /// <param name="utcNow">Current UTC time stamped on the raised domain event (ADR-0015).</param>
    /// <returns>A result indicating success or failure with forbidden error.</returns>
    /// <remarks>
    /// Possible raised events:
    /// <list type="bullet">
    /// <item><see cref="FeedbackChangedDomainEvent"/>: Raised when feedback text or rating is modified (not raised if values are unchanged).</item>
    /// </list>
    /// </remarks>
    public Result ChangeFeedback(
        FeedbackText feedback,
        FeedbackRating rating,
        Guid byUserId,
        DateTimeOffset utcNow)
    {
        if (CreatedByUser != byUserId)
        {
            return Result.Fail(FeedbackErrors.Forbidden(byUserId));
        }

        var oldFeedbackText = FeedbackText;
        var oldRating = Rating;
        if (oldFeedbackText == feedback && oldRating == rating)
        {
            return Result.Ok();
        }

        FeedbackText = feedback;
        Rating = rating;

        AddDomainEvent(
            new FeedbackChangedDomainEvent
            {
                OccurredOnUtc = utcNow,
                FeedbackId = Id,
                UserId = CreatedByUser,
                NewRating = rating,
                OldRating = oldRating,
                NewText = feedback,
                OldText = oldFeedbackText
            });

        return Result.Ok();
    }

    public DateTimeOffset CreatedUtc { get; private set; }

    public DateTimeOffset LastModifiedUtc { get; private set; }
}
