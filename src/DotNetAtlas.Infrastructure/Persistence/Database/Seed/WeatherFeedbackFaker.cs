using Bogus;
using DotNetAtlas.Domain.Entities.Weather.Feedback;
using DotNetAtlas.Domain.Entities.Weather.Feedback.ValueObjects;

namespace DotNetAtlas.Infrastructure.Persistence.Database.Seed;

public sealed class WeatherFeedbackFaker : Faker<Feedback>
{
    public WeatherFeedbackFaker()
    {
        CustomInstantiator(f => new Feedback(
            FeedbackText.Create(f.Lorem.Sentence()).Value,
            FeedbackRating.Create(f.Random.Int(1, 5)).Value,
            f.Random.Guid()
        ));

        var utcNow = DateTimeOffset.UtcNow;
        RuleFor(wf => wf.FeedbackText, f => FeedbackText.Create(f.Lorem.Sentence(5, 2)).Value)
            .RuleFor(aci => aci.CreatedUtc, _ => utcNow)
            .RuleFor(aci => aci.LastModifiedUtc, _ => utcNow);
    }
}
