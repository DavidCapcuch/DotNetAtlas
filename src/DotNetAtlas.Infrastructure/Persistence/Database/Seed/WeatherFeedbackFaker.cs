using Bogus;
using DotNetAtlas.Domain.Entities.Weather.Feedback;

namespace DotNetAtlas.Infrastructure.Persistence.Database.Seed;

public sealed class WeatherFeedbackFaker : Faker<WeatherFeedback>
{
    public WeatherFeedbackFaker()
    {
        var utcNow = DateTime.UtcNow;
        RuleFor(aci => aci.Id, _ => Guid.CreateVersion7())
        .RuleFor(wf => wf.Feedback, f => FeedbackText.Create(f.Lorem.Sentence(5, 2)).Value)
        .RuleFor(wf => wf.Rating, f => FeedbackRating.Create(f.Random.Byte(1, 5)).Value)
        .RuleFor(aci => aci.CreatedByUser, _ => Guid.CreateVersion7())
        .RuleFor(aci => aci.CreatedUtc, _ => utcNow)
        .RuleFor(aci => aci.LastModifiedUtc, _ => utcNow);
    }
}
