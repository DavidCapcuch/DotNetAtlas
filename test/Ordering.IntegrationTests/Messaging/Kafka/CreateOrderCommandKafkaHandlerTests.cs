using Avro;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Messaging.Kafka.SagaCommands;
using Ordering.Infrastructure.Persistence.Database;
using Ordering.IntegrationTests.Common;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;
using Platform.Test.Framework.Assertions;
using Platform.Test.Framework.Kafka;
using AvroCreateOrderCommand = Ordering.Orders.CreateOrderCommand;
using AvroCreateOrderItem = Ordering.Orders.CreateOrderItem;
using AvroOrderAddress = Ordering.Orders.OrderAddress;
using AvroOrderCreatedEvent = Ordering.Orders.OrderCreatedEvent;

namespace Ordering.IntegrationTests.Messaging.Kafka;

/// <summary>
/// Acceptance for <see cref="CreateOrderCommandKafkaHandler"/> — drives
/// the Kafka handler directly with a synthetic
/// <see cref="FakeKafkaMessageContext"/> and an Avro
/// <see cref="AvroCreateOrderCommand"/>; assertions cover the mapped
/// application command's side effects (Order persisted via
/// <see cref="OrderingDbContext"/> + <c>OrderCreatedEvent</c> captured by
/// the <see cref="FakeOutboxWriter"/>). Creation has no HTTP entrance — the saga drives it over
/// Kafka — so this happy path also carries the persistence-mapping assertions.
/// Mirrors Inventory's precedent at
/// <c>test/Inventory.IntegrationTests/Messaging/Kafka/ReserveStockCommandKafkaHandlerTests.cs</c>.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class CreateOrderCommandKafkaHandlerTests : BaseIntegrationTest
{
    public CreateOrderCommandKafkaHandlerTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    [Trait("Category", "critical-path")]
    public async Task HappyPath_AvroCommandTranslatedAndOrderCreatedWithOutboxRow()
    {
        // Every mapping axis gets a non-default value — USD, distinct shipping/billing, non-null
        // Street2/State, a pinned request time — so a converter or owned mapping that only handles
        // the seed defaults, or swaps two same-typed fields, fails here.
        var avro = NewValidAvroCommand(currency: "USD");
        avro.RequestedAtUtc = new DateTime(2026, 4, 23, 10, 0, 0, DateTimeKind.Utc);
        avro.ShippingAddress = NewValidAvroAddress(
            street1: "221B Baker Street", street2: "Flat B", city: "London", state: "Greater London",
            postalCode: "NW1 6XE", countryCode: "GB");
        avro.BillingAddress = NewValidAvroAddress(
            street1: "10 Rue de Rivoli", street2: null, city: "Paris", state: null,
            postalCode: "75001", countryCode: "FR");

        var fakeOutbox = Fixture.GetFakeOutbox();
        fakeOutbox.Clear();

        // Snapshot wall-clock before the act: the audit interceptor reads TimeProvider.System,
        // which ADR-0015 deliberately leaves real in the host, so a bounded window is the oracle.
        var beforeAct = DateTimeOffset.UtcNow;

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateOrderCommandKafkaHandler>();
        var ctx = FakeKafkaMessageContext.Create(
            cancellationToken: TestContext.Current.CancellationToken);

        await handler.Handle(ctx, avro);

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        using (new AssertionScope())
        {
            var saved = await db.Orders.AsNoTracking()
                .FirstAsync(o => o.Id == avro.OrderId, TestContext.Current.CancellationToken);
            saved.Id.Should().Be(avro.OrderId,
                "the client-assigned OrderId (ADR-0029) is persisted as the order's identity");
            saved.Status.Should().Be(OrderStatus.Created);
            saved.BuyerId.Should().Be(avro.BuyerId);
            saved.PaymentMethodId.Should().Be(avro.PaymentMethodId);

            // Owned Money: the line total is derived, not carried on the wire.
            saved.Total.Amount.Should().Be(19.98m);
            saved.Total.Currency.Should().Be(CurrencyCode.Usd);

            saved.ShippingAddress.Should().BeEquivalentTo(new
            {
                Street1 = "221B Baker Street",
                Street2 = "Flat B",
                City = "London",
                State = "Greater London",
                PostalCode = "NW1 6XE",
                CountryCode = "GB",
            });
            saved.BillingAddress.Should().BeEquivalentTo(new
            {
                Street1 = "10 Rue de Rivoli",
                Street2 = (string?)null,
                City = "Paris",
                State = (string?)null,
                PostalCode = "75001",
                CountryCode = "FR",
            });

            // The owned OrderItem collection auto-loads with the root (no Include needed).
            saved.Items.Should().ContainSingle()
                .Which.Should().BeEquivalentTo(new
                {
                    ProductId = avro.Items[0].ProductId,
                    Quantity = 2,
                    UnitPrice = new { Amount = 9.99m, Currency = CurrencyCode.Usd },
                    LineTotal = new { Amount = 19.98m, Currency = CurrencyCode.Usd },
                    ProductSnapshot = new { Sku = "SKU-1", Name = "Test widget" },
                });

            // The command's own timestamp becomes the domain CreatedAtUtc (ADR-0015 trace fidelity)...
            saved.CreatedAtUtc.Should().Be(new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero));
            // ...while the audit interceptor stamps from the host clock on SaveChanges.
            saved.CreatedUtc.Should().BeCloseTo(beforeAct, TimeSpan.FromSeconds(5));
            saved.LastModifiedUtc.Should().BeCloseTo(beforeAct, TimeSpan.FromSeconds(5));

            // The OrderCreatedOutboxPublisherDomainEventHandler ran on
            // SaveChanges and translated the internal *DomainEvent into
            // an Avro OrderCreatedEvent on the FakeOutboxWriter.
            fakeOutbox.GetMessages<AvroOrderCreatedEvent>()
                .Should().ContainSingle(m => m.IntegrationEvent.OrderId == saved.Id,
                    "outbox publisher must emit exactly one OrderCreatedEvent for the new order");

            // The capture is taken before commit; the row proves it committed with the order.
            var outboxRows = await db.Set<OutboxMessage>().AsNoTracking()
                .Where(m => m.KafkaKey == saved.Id.ToString())
                .ToListAsync(TestContext.Current.CancellationToken);
            outboxRows.Should().ContainSingle()
                .Which.TopicName.Should().Be("ordering.orders");
            outboxRows[0].Type.Should().BeMessageType<AvroOrderCreatedEvent>();
        }
    }

    [Fact]
    public async Task DuplicateOrderId_HandlerIsIdempotent_NoDoubleEmit()
    {
        var avro = NewValidAvroCommand();
        var fakeOutbox = Fixture.GetFakeOutbox();
        fakeOutbox.Clear();

        using (var scope = Fixture.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<CreateOrderCommandKafkaHandler>();
            await handler.Handle(
                FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
                avro);
        }

        // Fresh scope for the second dispatch — same as a Kafka redelivery
        // landing on a different consumer scope.
        using (var scope = Fixture.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<CreateOrderCommandKafkaHandler>();
            await handler.Handle(
                FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
                avro);
        }

        using (new AssertionScope())
        {
            // Handler short-circuits on the OrderId pre-check (the client-assigned PK, ADR-0029),
            // so the second dispatch does NOT raise the OrderCreatedDomainEvent again. Filter
            // by OrderId for parallel-safety (other tests in the
            // collection share the singleton FakeOutboxWriter).
            fakeOutbox.GetMessages<AvroOrderCreatedEvent>()
                .Where(m => m.IntegrationEvent.OrderId == avro.OrderId)
                .Should().HaveCount(1, "redelivery must short-circuit on the OrderId pre-check");

            using var verifyScope = Fixture.CreateScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var orderCount = await db.Orders.AsNoTracking()
                .CountAsync(o => o.Id == avro.OrderId, TestContext.Current.CancellationToken);
            orderCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task MultiCurrencyItems_ThrowsDataIntegrityException_BypassesSagaCommandWrapping()
    {
        var avro = NewValidAvroCommand();
        avro.Items.Add(new AvroCreateOrderItem
        {
            ProductId = Guid.CreateVersion7(),
            Sku = "SKU-USD",
            Name = "USD widget",
            Quantity = 1,
            UnitPriceAmount = new AvroDecimal(5m),
            UnitPriceCurrency = "USD",
        });

        var fakeOutbox = Fixture.GetFakeOutbox();
        fakeOutbox.Clear();

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateOrderCommandKafkaHandler>();
        var ctx = FakeKafkaMessageContext.Create(
            cancellationToken: TestContext.Current.CancellationToken);

        // SagaCommandMappers.ResolveUniformCurrency throws
        // DataIntegrityException — bug-class, NOT wrapped by
        // SagaCommandHandlerBase (which only wraps Result.Fail).
        // Propagates so KafkaFlow's DLT middleware can route the message.
        var act = () => handler.Handle(ctx, avro);
        var thrown = await act.Should().ThrowAsync<DataIntegrityException>();
        thrown.Which.ErrorCode.Should().Be("Ordering.MultipleCurrencies");

        // Pin the rollback contract: the throw happens during Avro→app
        // translation, BEFORE the handler reaches the DbContext.Add call,
        // so no Order row and no outbox row may exist.
        using (new AssertionScope())
        using (var verifyScope = Fixture.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            (await db.Orders.AsNoTracking()
                .AnyAsync(o => o.Id == avro.OrderId, TestContext.Current.CancellationToken))
                .Should().BeFalse("multi-currency rejection must abort before persistence");

            fakeOutbox.GetMessages<AvroOrderCreatedEvent>()
                .Where(m => m.IntegrationEvent.OrderId == avro.OrderId)
                .Should().BeEmpty();
        }
    }

    [Fact]
    public async Task NoItems_ResultFailFromValidator_WrappedInSagaCommandDispatchException()
    {
        var avro = NewValidAvroCommand();
        avro.Items.Clear();

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateOrderCommandKafkaHandler>();
        var ctx = FakeKafkaMessageContext.Create(
            cancellationToken: TestContext.Current.CancellationToken);

        // CreateOrderCommandValidator's "Items.NotEmpty" rule fails inside
        // the ValidationBehavior, which translates into Result.Fail. The
        // SagaCommandHandlerBase observes Result.IsFailed and throws
        // SagaCommandDispatchException → poison-pill DLT path.
        var act = () => handler.Handle(ctx, avro);
        await act.Should().ThrowAsync<SagaCommandDispatchException>();
    }

    private AvroCreateOrderCommand NewValidAvroCommand(string currency = "EUR") => new()
    {
        // Client-assigned OrderId (ADR-0029) flows into the persisted PK.
        OrderId = Guid.CreateVersion7(),
        BuyerId = Guid.CreateVersion7(),
        PaymentMethodId = Guid.CreateVersion7(),
        Items = new List<AvroCreateOrderItem>
        {
            new()
            {
                ProductId = Guid.CreateVersion7(),
                Sku = "SKU-1",
                Name = "Test widget",
                Quantity = 2,
                UnitPriceAmount = new AvroDecimal(9.99m),
                UnitPriceCurrency = currency,
            },
        },
        ShippingAddress = NewValidAvroAddress(),
        BillingAddress = NewValidAvroAddress(),
        RequestedAtUtc = DateTime.UtcNow,
    };

    private static AvroOrderAddress NewValidAvroAddress(
        string street1 = "1 Test Way",
        string? street2 = null,
        string city = "Prague",
        string? state = null,
        string postalCode = "11000",
        string countryCode = "CZ") => new()
        {
            Street1 = street1,
            Street2 = street2,
            City = city,
            State = state,
            PostalCode = postalCode,
            CountryCode = countryCode,
        };
}
