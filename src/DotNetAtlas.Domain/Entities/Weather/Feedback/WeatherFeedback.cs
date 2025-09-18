using DotNetAtlas.Domain.Entities.Base;

namespace DotNetAtlas.Domain.Entities.Weather.Feedback;

public sealed class WeatherFeedback : Entity<Guid>, IAuditableEntity
{
    public FeedbackText Feedback { get; private set; } = null!;

    public FeedbackRating Rating { get; private set; } = null!;

    public Guid CreatedByUser { get; private set; }

    public WeatherFeedback()
    {
    }

    public WeatherFeedback(FeedbackText feedback, FeedbackRating rating, Guid createdByUser)
    {
        Id = Guid.CreateVersion7();
        Feedback = feedback;
        Rating = rating;
        CreatedByUser = createdByUser;
    }

    public void UpdateFeedback(FeedbackText feedback)
    {
        Feedback = feedback;
    }

    public void UpdateRating(FeedbackRating rating)
    {
        Rating = rating;
    }

    public DateTime CreatedUtc { get; set; }

    public DateTime LastModifiedUtc { get; set; }
}
