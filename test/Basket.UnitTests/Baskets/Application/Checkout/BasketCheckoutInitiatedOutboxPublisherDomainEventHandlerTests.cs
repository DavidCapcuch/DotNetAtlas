using System.Collections.Immutable;
using Basket.Application.Baskets.Checkout;
using Basket.Application.Common.Data;
using Basket.Application.Common.Messaging;
using Basket.Domain.Baskets.Events;
using Basket.Domain.Baskets.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.ValueObjects;

namespace Basket.UnitTests.Baskets.Application.Checkout;

/// <summary>
/// Verifies the in-process domain-event handler routes the checkout event to the
/// transactional outbox with the correct topic, key, and Avro payload type.
/// </summary>
public class BasketCheckoutInitiatedOutboxPublisherDomainEventHandlerTests
{
    [Fact]
    public async Task Handle_AddsOutboxMessageWithBasketSessionsTopicAndUserIdKey()
    {
        var outbox = Substitute.For<ITransactionalOutbox<IBasketDbContext>>();
        var topicsOptions = Options.Create(new TopicsOptions
        {
            BasketSessions = "basket.sessions",
            DltTopicSuffix = ".DLT",
        });
        var sut = new BasketCheckoutInitiatedOutboxPublisherDomainEventHandler(
            outbox,
            topicsOptions,
            NullLogger<BasketCheckoutInitiatedOutboxPublisherDomainEventHandler>.Instance);

        var userId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var capturedAt = new DateTimeOffset(2026, 01, 15, 09, 30, 00, TimeSpan.Zero);
        var snapshot = new ProductSnapshot("SKU", "N", new Money(10m, CurrencyCode.Usd), capturedAt);
        var item = new BasketItem(productId, snapshot, 1);
        var basketSnapshot = new BasketSnapshot(
            ImmutableArray.Create(item),
            new BasketTotal(new Money(10m, CurrencyCode.Usd)));
        var address = Address.Create("S", null, "C", null, "P", "US").Value;

        var ev = new BasketCheckedOutDomainEvent
        {
            UserId = userId,
            CorrelationId = correlationId,
            Snapshot = basketSnapshot,
            ShippingAddress = address,
            BillingAddress = address,
            PaymentMethodId = Guid.CreateVersion7(),
        };

        await sut.Handle(ev, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            outbox.Received(1).AddOutboxMessage(
                Arg.Is<string>(t => t == "basket.sessions"),
                Arg.Is<string>(k => k == userId.ToString()),
                Arg.Is<Basket.Sessions.BasketCheckoutInitiatedEvent>(e =>
                    e.BasketCorrelationId == correlationId
                    && e.UserId == userId
                    && e.Items.Count == 1));
        }
    }
}
