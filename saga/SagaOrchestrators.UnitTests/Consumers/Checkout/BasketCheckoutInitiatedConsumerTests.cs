using System.Text.Json;
using Basket.Sessions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SagaOrchestrators.Checkout.CheckoutSaga.Consumers;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.UnitTests.Consumers.Checkout;

/// <summary>
/// Asserts <see cref="BasketCheckoutInitiatedConsumer"/> renames Basket's
/// <c>BasketCorrelationId</c> onto the saga record's <c>CorrelationId</c>, serialises items +
/// addresses to JSON, and never emits the Avro decimal byte-array shape (PII per ADR-0011 also
/// covered by the consumer's log allowlist - not asserted here, see code review). Uses a
/// mocked <see cref="ConsumeContext{T}"/> because <see cref="Avro.AvroDecimal"/> does not
/// round-trip through the harness's in-memory message serializer.
/// </summary>
public class BasketCheckoutInitiatedConsumerTests
{
    [Fact]
    public async Task Consume_publishes_initiator_saga_event_with_mapped_fields_and_json_snapshot()
    {
        var basketCorrelationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var initiatedAt = DateTime.SpecifyKind(new DateTime(2026, 5, 3, 11, 45, 0), DateTimeKind.Utc);

        var avro = new BasketCheckoutInitiatedEvent
        {
            BasketCorrelationId = basketCorrelationId,
            UserId = userId,
            Items =
            [
                new BasketCheckoutItem
                {
                    ProductId = productId,
                    Sku = "SKU-001",
                    Name = "Widget",
                    UnitPriceAmount = new Avro.AvroDecimal(19.99m),
                    UnitPriceCurrency = "USD",
                    Quantity = 2,
                    LineTotal = new Avro.AvroDecimal(39.98m)
                }
            ],
            TotalAmount = new Avro.AvroDecimal(39.98m),
            Currency = "USD",
            ShippingAddress = new CheckoutAddress
            {
                Street1 = "1 Pine St",
                Street2 = null,
                City = "Seattle",
                State = "WA",
                PostalCode = "98101",
                CountryCode = "US"
            },
            BillingAddress = new CheckoutAddress
            {
                Street1 = "1 Pine St",
                Street2 = "Apt 4",
                City = "Seattle",
                State = "WA",
                PostalCode = "98101",
                CountryCode = "US"
            },
            PaymentMethodId = paymentMethodId,
            InitiatedAtUtc = initiatedAt
        };

        BasketCheckoutInitiatedSagaEvent? captured = null;
        var ctx = Substitute.For<ConsumeContext<BasketCheckoutInitiatedEvent>>();
        ctx.Message.Returns(avro);
        ctx.Publish(Arg.Do<BasketCheckoutInitiatedSagaEvent>(e => captured = e), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var consumer = new BasketCheckoutInitiatedConsumer(NullLogger<BasketCheckoutInitiatedConsumer>.Instance);

        await consumer.Consume(ctx);

        Assert.NotNull(captured);
        Assert.Equal(basketCorrelationId, captured.CorrelationId);
        Assert.Equal(userId, captured.UserId);
        Assert.Equal(39.98m, captured.TotalAmount);
        Assert.Equal("USD", captured.Currency);
        Assert.Equal(paymentMethodId, captured.PaymentMethodId);
        Assert.Equal(new DateTimeOffset(initiatedAt, TimeSpan.Zero), captured.InitiatedAtUtc);

        // JSON snapshot fields parse back to the expected shape.
        using var snapshot = JsonDocument.Parse(captured.BasketSnapshotJson);
        Assert.Equal(1, snapshot.RootElement.GetArrayLength());
        var item = snapshot.RootElement[0];
        Assert.Equal(productId, item.GetProperty("ProductId").GetGuid());
        Assert.Equal("SKU-001", item.GetProperty("Sku").GetString());
        Assert.Equal(2, item.GetProperty("Quantity").GetInt32());
        Assert.Equal(19.99m, item.GetProperty("UnitPriceAmount").GetDecimal());
        Assert.Equal(39.98m, item.GetProperty("LineTotal").GetDecimal());

        Assert.NotNull(captured.ShippingAddressJson);
        Assert.NotNull(captured.BillingAddressJson);
        using var billing = JsonDocument.Parse(captured.BillingAddressJson!);
        Assert.Equal("Apt 4", billing.RootElement.GetProperty("Street2").GetString());
    }
}
