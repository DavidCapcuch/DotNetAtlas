using DotNetAtlas.Domain.Common;
using DotNetAtlas.Domain.Entities.Weather.Feedback.Events;
using DotNetAtlas.Domain.Entities.Weather.Feedback.ValueObjects;

namespace DotNetAtlas.Domain.Entities.Weather.Feedback;

public sealed class Feedback : AggregateRoot<Guid>, IAuditableEntity
{
    public FeedbackText FeedbackText { get; private set; } = null!;

    public FeedbackRating Rating { get; private set; } = null!;

    public Guid CreatedByUser { get; private set; }

    // For EF Core
    private Feedback()
    {
    }

    public Feedback(FeedbackText feedbackText, FeedbackRating rating, Guid createdByUser)
    {
        Id = Guid.CreateVersion7();
        FeedbackText = feedbackText;
        Rating = rating;
        CreatedByUser = createdByUser;

        RaiseDomainEvent(
            new FeedbackCreatedDomainEvent
            {
                FeedbackId = Id,
                UserId = CreatedByUser,
                Rating = rating.Value,
                Text = feedbackText.Value,
                OccurredOnUtc = DateTimeOffset.UtcNow
            });
    }

    public void ChangeFeedback(FeedbackText feedback, FeedbackRating rating)
    {
        var oldFeedbackText = FeedbackText.Value;
        var oldRating = Rating.Value;
        if (oldFeedbackText == feedback.Value && oldRating == rating.Value)
        {
            return;
        }

        FeedbackText = feedback;
        Rating = rating;

        RaiseDomainEvent(
            new FeedbackChangedDomainEvent
            {
                FeedbackId = Id,
                UserId = CreatedByUser,
                NewRating = rating.Value,
                OldRating = oldRating,
                NewText = feedback.Value,
                OldText = oldFeedbackText,
                OccurredOnUtc = DateTimeOffset.UtcNow
            });
    }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset LastModifiedUtc { get; set; }
}
