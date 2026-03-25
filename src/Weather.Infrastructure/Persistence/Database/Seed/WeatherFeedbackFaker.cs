using Bogus;
using Weather.Domain.Feedback.ValueObjects;

namespace Weather.Infrastructure.Persistence.Database.Seed;

public sealed class WeatherFeedbackFaker : Faker<Weather.Domain.Feedback.Feedback>
{
    public WeatherFeedbackFaker()
    {
        // Use private constructor via reflection to bypass domain event firing
        CustomInstantiator(_ =>
            (Weather.Domain.Feedback.Feedback)Activator.CreateInstance(typeof(Weather.Domain.Feedback.Feedback),
                nonPublic: true)!);

        var utcNow = DateTimeOffset.UtcNow;

        RuleFor(wf => wf.Id, _ => Guid.CreateVersion7())
            .RuleFor(wf => wf.FeedbackText, f => FeedbackText.Create(f.Lorem.Sentence(5, 2)).Value)
            .RuleFor(wf => wf.Rating, f => FeedbackRating.Create(f.Random.Byte(1, 5)).Value)
            .RuleFor(wf => wf.CreatedByUser, f => f.Random.Guid())
            .RuleFor(wf => wf.CreatedUtc, _ => utcNow)
            .RuleFor(wf => wf.LastModifiedUtc, _ => utcNow);
    }
}
