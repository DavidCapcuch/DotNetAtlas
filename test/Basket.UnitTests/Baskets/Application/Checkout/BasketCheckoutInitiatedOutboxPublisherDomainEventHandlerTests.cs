using System.Collections.Immutable;
using Basket.Application.Baskets.Checkout;
using Basket.Application.Common.Data;
using Basket.Application.Common.Messaging;
using Basket.Domain.Baskets.Events;
using Basket.Domain.Baskets.ValueObjects;
using Microsoft.Extensions.Logging;
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
        // Arrange
        var outbox = Substitute.For<ITransactionalOutbox<IBasketDbContext>>();
        var topicsOptions = Options.Create(new TopicsOptions
        {
            BasketSessions = "basket.sessions",
        });
        var sut = new BasketCheckoutInitiatedOutboxPublisherDomainEventHandler(
            outbox,
            topicsOptions,
            NullLogger<BasketCheckoutInitiatedOutboxPublisherDomainEventHandler>.Instance);

        var userId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var capturedAt = new DateTimeOffset(2026, 01, 15, 09, 30, 00, TimeSpan.Zero);
        var snapshot = ProductSnapshot.Create("SKU", "N", Money.Create(10m, CurrencyCode.Usd).Value, capturedAt);
        var item = BasketItem.BuildUnchecked(productId, snapshot, 1);
        var basketSnapshot = BasketSnapshot.Create(
            ImmutableArray.Create(item),
            BasketTotal.From(Money.Create(10m, CurrencyCode.Usd).Value));
        var address = Address.Create("S", null, "C", null, "P", "US").Value;

        var checkedOutEvent = new BasketCheckedOutDomainEvent
        {
            OccurredOnUtc = capturedAt,
            UserId = userId,
            OrderId = orderId,
            Snapshot = basketSnapshot,
            ShippingAddress = address,
            BillingAddress = address,
            PaymentMethodId = Guid.CreateVersion7(),
        };

        // Act
        await sut.Handle(checkedOutEvent, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            outbox.Received(1).AddOutboxMessage(
                Arg.Is<string>(t => t == "basket.sessions"),
                Arg.Is<string>(k => k == userId.ToString()),
                Arg.Is<Basket.Sessions.BasketCheckoutInitiatedEvent>(e =>
                    e.OrderId == orderId
                    && e.UserId == userId
                    && e.Items.Count == 1));
        }
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task Handle_LogsAtDebugWithQueuedVerb_NotInformation()
    {
        // sum2.H-3 regression guard. The pre-fix line was a LogInformation "Added ..."
        // emitted from inside IDomainEventHandler.Handle — i.e. BEFORE the command
        // handler's SaveChangesAsync. Splunk / Grafana dashboards that counted
        // "checkouts initiated" via that information-level line would over-count on
        // any subsequent SaveChanges failure. The publisher now logs at Debug with
        // "Queued" verb; CheckoutBasketCommandHandler emits the post-commit
        // information-level "Published" line.

        // Arrange
        var logger = Substitute.For<ILogger<BasketCheckoutInitiatedOutboxPublisherDomainEventHandler>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var outbox = Substitute.For<ITransactionalOutbox<IBasketDbContext>>();
        var topicsOptions = Options.Create(new TopicsOptions
        {
            BasketSessions = "basket.sessions",
        });
        var sut = new BasketCheckoutInitiatedOutboxPublisherDomainEventHandler(
            outbox,
            topicsOptions,
            logger);

        var capturedAt = new DateTimeOffset(2026, 01, 15, 09, 30, 00, TimeSpan.Zero);
        var snapshot = ProductSnapshot.Create("SKU", "N", Money.Create(10m, CurrencyCode.Usd).Value, capturedAt);
        var item = BasketItem.BuildUnchecked(Guid.CreateVersion7(), snapshot, 1);
        var basketSnapshot = BasketSnapshot.Create(
            ImmutableArray.Create(item),
            BasketTotal.From(Money.Create(10m, CurrencyCode.Usd).Value));
        var address = Address.Create("S", null, "C", null, "P", "US").Value;
        var checkedOutEvent = new BasketCheckedOutDomainEvent
        {
            OccurredOnUtc = capturedAt,
            UserId = Guid.CreateVersion7(),
            OrderId = Guid.CreateVersion7(),
            Snapshot = basketSnapshot,
            ShippingAddress = address,
            BillingAddress = address,
            PaymentMethodId = Guid.CreateVersion7(),
        };

        // Act
        await sut.Handle(checkedOutEvent, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            logger.Received().Log(
                LogLevel.Debug,
                Arg.Any<EventId>(),
                Arg.Is<object>(state => state.ToString()!.Contains("Queued", StringComparison.Ordinal)),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>());

            logger.DidNotReceive().Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>());
        }
    }
}
