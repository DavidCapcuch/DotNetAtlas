using Bogus;
using Ordering.Domain;
using Ordering.Domain.AlertSubscriptionOrders;
using Ordering.Domain.ValueObjects;

namespace Ordering.Infrastructure.Common.Persistence.Database.Seed;

public sealed class AlertSubscriptionOrderFaker : Faker<AlertSubscriptionOrder>
{
    private static readonly AlertSubscriptionTier[] PaidTiers =
        [AlertSubscriptionTier.Pro, AlertSubscriptionTier.Ultra];

    private static readonly CurrencyCode[] Currencies =
        [CurrencyCode.Usd, CurrencyCode.Eur, CurrencyCode.Gbp];

    public AlertSubscriptionOrderFaker()
    {
        // Use private constructor via reflection to bypass domain event firing
        CustomInstantiator(_ =>
            (AlertSubscriptionOrder)Activator.CreateInstance(typeof(AlertSubscriptionOrder), nonPublic: true)!);

        var utcNow = DateTime.UtcNow;

        RuleFor(o => o.Id, _ => Guid.CreateVersion7())
            .RuleFor(o => o.UserId, f => f.Random.Guid())
            .RuleFor(o => o.AlertSubscriptionOrderType, f => f.PickRandom<AlertSubscriptionOrderType>())
            .RuleFor(o => o.PaymentMethodId, f => f.Random.Guid())
            .RuleFor(o => o.Tier, (f, o) => o.AlertSubscriptionOrderType == AlertSubscriptionOrderType.Purchase
                ? f.PickRandom(PaidTiers)
                : null)
            .RuleFor(o => o.DurationDays, f => f.PickRandom(30, 90, 180, 365))
            .RuleFor(o => o.Price, (f, _) =>
                Money.Create(f.Finance.Amount(9.99m, 299.99m), f.PickRandom(Currencies)).Value)
            .RuleFor(o => o.Status, _ => AlertSubscriptionOrderStatus.Initiated)
            .RuleFor(o => o.CreatedAtUtc, _ => utcNow);
    }
}
