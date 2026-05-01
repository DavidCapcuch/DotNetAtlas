using Avro;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Infrastructure.Messaging.Kafka.SagaCommands;
using Ordering.Infrastructure.Persistence.Database;
using Ordering.IntegrationTests.Common;
using AvroCreateOrderCommand = Ordering.Orders.CreateOrderCommand;
using AvroCreateOrderItem = Ordering.Orders.CreateOrderItem;
using AvroOrderAddress = Ordering.Orders.OrderAddress;
using AvroOrderCreatedEvent = Ordering.Orders.OrderCreatedEvent;

namespace Ordering.IntegrationTests.Messaging.Kafka;

/// <summary>
/// Pins the ADR-0008 / DoD line 121 chain: a saga-issued
/// <c>CreateOrderCommand</c> with a specific <c>CorrelationId</c> must
/// land in the <c>ordering.orders.correlation_id</c> column AND in the
/// emitted external <c>OrderCreatedEvent.CorrelationId</c>. Tests
/// invoke <see cref="CreateOrderCommandKafkaHandler"/> directly (no
/// KafkaFlow middleware pipeline), so the canonical source asserted
/// here is the Avro payload's <c>CorrelationId</c> per
/// <c>SagaCommandHandlerBase</c>; the Kafka <c>correlation-id</c>
/// header is set on the synthetic context for parity with production
/// but not asserted on.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class CorrelationIdPropagationTests
{
    private readonly IntegrationTestFixture _fixture;

    public CorrelationIdPropagationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CorrelationIdFlowsFromAvroPayloadIntoDbColumnAndEmittedEvent()
    {
        var correlationId = Guid.CreateVersion7();
        var fakeOutbox = _fixture.GetFakeOutbox();
        fakeOutbox.Clear();

        var avro = new AvroCreateOrderCommand
        {
            CorrelationId = correlationId,
            BuyerId = Guid.CreateVersion7(),
            PaymentMethodId = Guid.CreateVersion7(),
            Items = new List<AvroCreateOrderItem>
            {
                new()
                {
                    ProductId = Guid.CreateVersion7(),
                    Sku = "SKU-CORR",
                    Name = "Correlation widget",
                    Quantity = 1,
                    UnitPriceAmount = new AvroDecimal(20m),
                    UnitPriceCurrency = "EUR",
                },
            },
            ShippingAddress = NewAvroAddress(),
            BillingAddress = NewAvroAddress(),
            RequestedAtUtc = _fixture.FakeTime.GetUtcNow().UtcDateTime,
        };

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateOrderCommandKafkaHandler>();

        await handler.Handle(
            FakeKafkaMessageContext.Create(
                correlationId: correlationId,
                cancellationToken: TestContext.Current.CancellationToken),
            avro);

        using var verifyScope = _fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        using (new AssertionScope())
        {
            var saved = await db.Orders.AsNoTracking()
                .FirstAsync(o => o.CorrelationId == correlationId, TestContext.Current.CancellationToken);
            saved.CorrelationId.Should().Be(correlationId,
                "the order's correlation_id column must mirror the Avro payload");

            var emitted = fakeOutbox.GetMessages<AvroOrderCreatedEvent>()
                .Should().ContainSingle(m => m.IntegrationEvent.OrderId == saved.Id).Subject;
            emitted.IntegrationEvent.CorrelationId.Should().Be(correlationId,
                "the emitted external event must carry the same CorrelationId end-to-end");
        }
    }

    private static AvroOrderAddress NewAvroAddress() => new()
    {
        Street1 = "1 Correlation Lane",
        Street2 = null,
        City = "Prague",
        State = null,
        PostalCode = "11000",
        CountryCode = "CZ",
    };
}
