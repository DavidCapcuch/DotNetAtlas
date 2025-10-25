using DotNetAtlas.Domain.Common;
using DotNetAtlas.Domain.Entities.Weather.Feedback.ValueObjects;

namespace DotNetAtlas.Domain.Entities.Weather.Feedback;

public sealed class Feedback : Entity<Guid>, IAuditableEntity
{
    public FeedbackText FeedbackText { get; private set; } = null!;

    public FeedbackRating Rating { get; private set; } = null!;

    public Guid CreatedByUser { get; private set; }

    public Feedback()
    {
    }

    public Feedback(FeedbackText feedbackText, FeedbackRating rating, Guid createdByUser)
    {
        Id = Guid.CreateVersion7();
        FeedbackText = feedbackText;
        Rating = rating;
        CreatedByUser = createdByUser;
    }

    public void UpdateFeedback(FeedbackText feedback)
    {
        FeedbackText = feedback;
    }

    public void UpdateRating(FeedbackRating rating)
    {
        Rating = rating;
    }

    public DateTime CreatedUtc { get; set; }

    public DateTime LastModifiedUtc { get; set; }
}
