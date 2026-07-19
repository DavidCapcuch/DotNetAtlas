using Avro;
using Payments.Infrastructure.Messaging.Kafka.PaymentCommands;
using AvroAuthorizePaymentCommand = Payments.Transactions.AuthorizePaymentCommand;
using AvroCapturePaymentCommand = Payments.Transactions.CapturePaymentCommand;
using AvroRequestRefundCommand = Payments.Transactions.RequestRefundCommand;
using AvroVoidPaymentCommand = Payments.Transactions.VoidPaymentCommand;

namespace Payments.UnitTests.Infrastructure.Messaging.Kafka.PaymentCommands;

/// <summary>
/// Field-level mapping tests for <see cref="SagaCommandMappers"/>. Pins the cross-cutting
/// #255 invariant: the Payments aggregate's primary key MUST be the saga-issued
/// <c>PaymentTransactionId</c> (UUID v7 minted by the saga at initial state), not the saga
/// CorrelationId — collapsing the two onto one value breaks the "v7 PK" guarantee
/// documented on <c>PaymentTransaction.Id</c>.
/// </summary>
[Trait("Category", "regression")]
public class SagaCommandMappersTests
{
    [Fact]
    public void ToAppCommand_AuthorizePayment_UsesAvroPaymentTransactionId_AsPaymentId()
    {
        // Arrange
        var orderId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        paymentTransactionId.Should().NotBe(orderId,
            "the two ids must differ for the test to prove the saga-issued PaymentTransactionId is used");

        var avro = new AvroAuthorizePaymentCommand
        {
            PaymentTransactionId = paymentTransactionId,
            OrderId = orderId,
            UserId = Guid.CreateVersion7(),
            PaymentMethodId = "pm_test",
            Amount = new AvroDecimal(99.99m),
            Currency = "USD",
            IdempotencyKey = "idem-1",
            RequestedAtUtc = new DateTime(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc),
        };

        // Act
        var app = avro.ToAppCommand();

        // Assert
        using (new AssertionScope())
        {
            app.PaymentId.Should().Be(paymentTransactionId,
                "#255: PaymentId is the saga-issued PaymentTransactionId field");
            app.PaymentId.Should().NotBe(orderId,
                "regression net for the v1 collapse where PaymentId = OrderId");
            app.OrderId.Should().Be(orderId,
                "OrderId comes from the wire payload field (ADR-0029/0030)");
        }
    }

    [Fact]
    public void ToAppCommand_AuthorizePayment_CopiesAllOtherFieldsVerbatim()
    {
        // Arrange
        var paymentTransactionId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var avro = new AvroAuthorizePaymentCommand
        {
            PaymentTransactionId = paymentTransactionId,
            OrderId = orderId,
            UserId = userId,
            PaymentMethodId = "pm_abc123",
            Amount = new AvroDecimal(123.4567m),
            Currency = "EUR",
            IdempotencyKey = "idem-abc",
            RequestedAtUtc = new DateTime(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc),
        };

        // Act
        var app = avro.ToAppCommand();

        // Assert
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

    [Fact]
    public void ToAppCommand_CapturePayment_ResolvesByOrderId()
    {
        // Arrange
        // Capture carries no PaymentTransactionId, so the handler resolves the aggregate by OrderId
        // (the saga key, ADR-0029). The mapper sources it from the OrderId wire field.
        var orderId = Guid.CreateVersion7();
        var avro = new AvroCapturePaymentCommand
        {
            OrderId = orderId,
            UserId = Guid.CreateVersion7(),
            AuthorizationId = "auth-123",
            Amount = new AvroDecimal(42.50m),
            RequestedAtUtc = new DateTime(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc),
        };

        // Act
        var app = avro.ToAppCommand();

        // Assert
        using (new AssertionScope())
        {
            app.OrderId.Should().Be(orderId);
            app.AuthorizationId.Should().Be("auth-123");
        }
    }

    [Fact]
    public void ToAppCommand_VoidPayment_ResolvesByOrderId()
    {
        // Arrange
        var orderId = Guid.CreateVersion7();
        var avro = new AvroVoidPaymentCommand
        {
            OrderId = orderId,
            UserId = Guid.CreateVersion7(),
            AuthorizationId = "auth-456",
            Reason = "saga_compensation",
            RequestedAtUtc = new DateTime(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc),
        };

        // Act
        var app = avro.ToAppCommand();

        // Assert
        using (new AssertionScope())
        {
            app.OrderId.Should().Be(orderId);
            app.AuthorizationId.Should().Be("auth-456");
            app.Reason.Should().Be("saga_compensation");
        }
    }

    [Fact]
    public void ToAppCommand_RequestRefund_UsesWirePaymentTransactionId_AsPaymentId()
    {
        // Arrange
        // RequestRefund targets a specific transaction by id, so the handler resolves the aggregate
        // by primary key.
        var paymentTransactionId = Guid.CreateVersion7();
        var avro = new AvroRequestRefundCommand
        {
            UserId = Guid.CreateVersion7(),
            PaymentTransactionId = paymentTransactionId,
            Reason = "buyer_cancelled",
            RequestedAtUtc = new DateTime(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc),
        };

        // Act
        var app = avro.ToAppCommand();

        // Assert
        using (new AssertionScope())
        {
            app.PaymentId.Should().Be(paymentTransactionId);
            app.Reason.Should().Be("buyer_cancelled");
        }
    }
}
