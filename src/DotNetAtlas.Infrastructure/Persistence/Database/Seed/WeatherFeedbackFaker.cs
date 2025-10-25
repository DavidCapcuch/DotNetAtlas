using Bogus;
using DotNetAtlas.Domain.Entities.Weather.Feedback;
using DotNetAtlas.Domain.Entities.Weather.Feedback.ValueObjects;

namespace DotNetAtlas.Infrastructure.Persistence.Database.Seed;

public sealed class WeatherFeedbackFaker : Faker<Feedback>
{
    public WeatherFeedbackFaker()
    {
        var utcNow = DateTime.UtcNow;
        RuleFor(aci => aci.Id, _ => Guid.CreateVersion7())
        .RuleFor(wf => wf.FeedbackText, f => FeedbackText.Create(f.Lorem.Sentence(5, 2)).Value)
        .RuleFor(wf => wf.Rating, f => FeedbackRating.Create(f.Random.Byte(1, 5)).Value)
        .RuleFor(aci => aci.CreatedByUser, _ => Guid.CreateVersion7())
        .RuleFor(aci => aci.CreatedUtc, _ => utcNow)
        .RuleFor(aci => aci.LastModifiedUtc, _ => utcNow);
    }
}
