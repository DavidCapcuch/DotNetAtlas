using Bogus;
using Ordering.Domain.AlertSubscriptionOrders;

namespace Ordering.Infrastructure.Common.Persistence.Database.Seed;

public sealed class AlertSubscriptionOrderFaker : Faker<AlertSubscriptionOrder>
{
    public AlertSubscriptionOrderFaker()
    {
        // Use private constructor via reflection to bypass domain event firing
        CustomInstantiator(_ =>
            (AlertSubscriptionOrder)Activator.CreateInstance(typeof(AlertSubscriptionOrder), nonPublic: true)!);

        var utcNow = DateTimeOffset.UtcNow;

        RuleFor(wf => wf.Id, _ => Guid.CreateVersion7())
            .RuleFor(wf => wf.FeedbackText, f => FeedbackText.Create(f.Lorem.Sentence(5, 2)).Value)
            .RuleFor(wf => wf.Rating, f => FeedbackRating.Create(f.Random.Byte(1, 5)).Value)
            .RuleFor(wf => wf.CreatedByUser, f => f.Random.Guid())
            .RuleFor(wf => wf.CreatedUtc, _ => utcNow)
            .RuleFor(wf => wf.LastModifiedUtc, _ => utcNow);
    }
}
