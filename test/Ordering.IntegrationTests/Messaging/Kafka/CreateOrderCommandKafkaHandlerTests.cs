using Avro;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Messaging.Kafka.SagaCommands;
using Ordering.Infrastructure.Persistence.Database;
using Ordering.IntegrationTests.Common;
using Platform.SharedKernel.Exceptions;
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
/// the <see cref="FakeOutboxWriter"/>).
/// Mirrors Inventory's precedent at
/// <c>test/Inventory.IntegrationTests/Messaging/Kafka/ReserveStockCommandKafkaHandlerTests.cs</c>.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class CreateOrderCommandKafkaHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public CreateOrderCommandKafkaHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HappyPath_AvroCommandTranslatedAndOrderCreatedWithOutboxRow()
    {
        var avro = NewValidAvroCommand();
        var fakeOutbox = _fixture.GetFakeOutbox();
        fakeOutbox.Clear();

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateOrderCommandKafkaHandler>();
        var ctx = FakeKafkaMessageContext.Create(
            correlationId: avro.CorrelationId, cancellationToken: TestContext.Current.CancellationToken);

        await handler.Handle(ctx, avro);

        using var verifyScope = _fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        using (new AssertionScope())
        {
            var saved = await db.Orders.AsNoTracking()
                .FirstAsync(o => o.CorrelationId == avro.CorrelationId, TestContext.Current.CancellationToken);
            saved.Status.Should().Be(OrderStatus.Created);
            saved.BuyerId.Should().Be(avro.BuyerId);
            saved.PaymentMethodId.Should().Be(avro.PaymentMethodId);

            // The OrderCreatedOutboxPublisherDomainEventHandler ran on
            // SaveChanges and translated the internal *DomainEvent into
            // an Avro OrderCreatedEvent on the FakeOutboxWriter.
            fakeOutbox.GetMessages<AvroOrderCreatedEvent>()
                .Should().ContainSingle(m => m.IntegrationEvent.OrderId == saved.Id,
                    "outbox publisher must emit exactly one OrderCreatedEvent for the new order");
        }
    }

    [Fact]
    public async Task DuplicateCorrelationId_HandlerIsIdempotent_NoDoubleEmit()
    {
        var avro = NewValidAvroCommand();
        var fakeOutbox = _fixture.GetFakeOutbox();
        fakeOutbox.Clear();

        using (var scope = _fixture.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<CreateOrderCommandKafkaHandler>();
            await handler.Handle(
                FakeKafkaMessageContext.Create(correlationId: avro.CorrelationId, cancellationToken: TestContext.Current.CancellationToken),
                avro);
        }

        // Fresh scope for the second dispatch — same as a Kafka redelivery
        // landing on a different consumer scope.
        using (var scope = _fixture.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<CreateOrderCommandKafkaHandler>();
            await handler.Handle(
                FakeKafkaMessageContext.Create(correlationId: avro.CorrelationId, cancellationToken: TestContext.Current.CancellationToken),
                avro);
        }

        using (new AssertionScope())
        {
            // Handler short-circuits on the CorrelationId pre-check
            // (CreateOrderCommandHandler:46-55), so the second dispatch
            // does NOT raise the OrderCreatedDomainEvent again. Filter
            // by CorrelationId for parallel-safety (other tests in the
            // collection share the singleton FakeOutboxWriter).
            fakeOutbox.GetMessages<AvroOrderCreatedEvent>()
                .Where(m => m.IntegrationEvent.CorrelationId == avro.CorrelationId)
                .Should().HaveCount(1, "redelivery must short-circuit on the CorrelationId pre-check");

            using var verifyScope = _fixture.CreateScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var orderCount = await db.Orders.AsNoTracking()
                .CountAsync(o => o.CorrelationId == avro.CorrelationId, TestContext.Current.CancellationToken);
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

        var fakeOutbox = _fixture.GetFakeOutbox();
        fakeOutbox.Clear();

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateOrderCommandKafkaHandler>();
        var ctx = FakeKafkaMessageContext.Create(
            correlationId: avro.CorrelationId, cancellationToken: TestContext.Current.CancellationToken);

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
        using (var verifyScope = _fixture.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            (await db.Orders.AsNoTracking()
                .AnyAsync(o => o.CorrelationId == avro.CorrelationId, TestContext.Current.CancellationToken))
                .Should().BeFalse("multi-currency rejection must abort before persistence");

            fakeOutbox.GetMessages<AvroOrderCreatedEvent>()
                .Where(m => m.IntegrationEvent.CorrelationId == avro.CorrelationId)
                .Should().BeEmpty();
        }
    }

    [Fact]
    public async Task NoItems_ResultFailFromValidator_WrappedInSagaCommandDispatchException()
    {
        var avro = NewValidAvroCommand();
        avro.Items.Clear();

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateOrderCommandKafkaHandler>();
        var ctx = FakeKafkaMessageContext.Create(
            correlationId: avro.CorrelationId, cancellationToken: TestContext.Current.CancellationToken);

        // CreateOrderCommandValidator's "Items.NotEmpty" rule fails inside
        // the ValidationBehavior, which translates into Result.Fail. The
        // SagaCommandHandlerBase observes Result.IsFailed and throws
        // SagaCommandDispatchException → poison-pill DLT path.
        var act = () => handler.Handle(ctx, avro);
        await act.Should().ThrowAsync<SagaCommandDispatchException>();
    }

    private AvroCreateOrderCommand NewValidAvroCommand() => new()
    {
        CorrelationId = Guid.CreateVersion7(),
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
                UnitPriceCurrency = "EUR",
            },
        },
        ShippingAddress = NewValidAvroAddress(),
        BillingAddress = NewValidAvroAddress(),
        RequestedAtUtc = DateTime.UtcNow,
    };

    private static AvroOrderAddress NewValidAvroAddress() => new()
    {
        Street1 = "1 Test Way",
        Street2 = null,
        City = "Prague",
        State = null,
        PostalCode = "11000",
        CountryCode = "CZ",
    };
}
