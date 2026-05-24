using Avro;
using Payments.Infrastructure.Messaging.Kafka.PaymentCommands;
using AvroAuthorizePaymentCommand = Payments.Transactions.AuthorizePaymentCommand;

namespace Payments.UnitTests.Infrastructure.Messaging.Kafka.PaymentCommands;

/// <summary>
/// Field-level mapping tests for <see cref="SagaCommandMappers"/>. Pins the cross-cutting
/// wave1-followup #255 fix: the Payments aggregate's primary key MUST be the saga-issued
/// <c>PaymentTransactionId</c> (UUID v7 minted by the saga at initial state), not the saga
/// CorrelationId. Mapping the two onto the same value was a v1 collapse that broke the
/// "v7 PK" guarantee documented on <c>PaymentTransaction.Id</c>.
/// </summary>
public class SagaCommandMappersTests
{
    [Fact]
    public void ToAppCommand_AuthorizePayment_UsesAvroPaymentTransactionId_AsPaymentId()
    {
        var correlationId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        paymentTransactionId.Should().NotBe(correlationId,
            "the two ids must differ for the test to prove the saga-issued PaymentTransactionId is used");

        var avro = new AvroAuthorizePaymentCommand
        {
            CorrelationId = correlationId,
            PaymentTransactionId = paymentTransactionId,
            OrderId = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            PaymentMethodId = "pm_test",
            Amount = new AvroDecimal(99.99m),
            Currency = "USD",
            IdempotencyKey = "idem-1",
            RequestedAtUtc = new DateTime(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc),
        };

        var app = avro.ToAppCommand(correlationId);

        using (new AssertionScope())
        {
            app.PaymentId.Should().Be(paymentTransactionId,
                "ADR-0008 + wave1-followup #255: PaymentId is the saga-issued PaymentTransactionId field");
            app.PaymentId.Should().NotBe(correlationId,
                "regression net for the v1 collapse where PaymentId = CorrelationId");
            app.CorrelationId.Should().Be(correlationId,
                "CorrelationId comes from the authoritative Kafka header argument (ADR-0008)");
        }
    }

    [Fact]
    public void ToAppCommand_AuthorizePayment_CopiesAllOtherFieldsVerbatim()
    {
        var correlationId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var avro = new AvroAuthorizePaymentCommand
        {
            CorrelationId = correlationId,
            PaymentTransactionId = paymentTransactionId,
            OrderId = orderId,
            UserId = userId,
            PaymentMethodId = "pm_abc123",
            Amount = new AvroDecimal(123.4567m),
            Currency = "EUR",
            IdempotencyKey = "idem-abc",
            RequestedAtUtc = new DateTime(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc),
        };

        var app = avro.ToAppCommand(correlationId);

        using (new AssertionScope())
        {
            app.OrderId.Should().Be(orderId);
            app.BuyerId.Should().Be(userId);
            app.Amount.Should().Be(123.4567m);
            app.Currency.Should().Be("EUR");
            app.PaymentMethodId.Should().Be("pm_abc123");
            app.IdempotencyKey.Should().Be("idem-abc");
        }
    }
}
