using AwesomeAssertions;
using Invoicing.Application.Invoices.Projections;
using Invoicing.Infrastructure.Messaging.Kafka.Projections;
using Invoicing.Infrastructure.Persistence.Database;
using Invoicing.IntegrationTests.Common;
using KafkaFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using AvroOrderConfirmedEvent = Ordering.Orders.OrderConfirmedEvent;
using AvroPaymentCapturedEvent = Payments.Transactions.PaymentCapturedEvent;

namespace Invoicing.IntegrationTests.Projections;

/// <summary>
/// Integration tests for the M6 invoice-side enrichment projection. Cover the
/// three convergence sessions from <c>example-mapping/invoicing.md § 1</c>:
/// order-first (1.1), payment-first (1.2), duplicate-order idempotency (1.3).
/// Handlers are exercised directly with an NSubstitute <c>IMessageContext</c>;
/// the inbox middleware that fronts the consumers in production is covered
/// by Platform.KafkaFlow.Inbox.EFCore's own tests.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class PendingInvoiceProjectionTests
{
    private static readonly DateTimeOffset OrderArrivalUtc =
        new(2026, 04, 26, 10, 00, 00, TimeSpan.Zero);

    private static readonly DateTimeOffset PaymentArrivalUtc =
        new(2026, 04, 26, 10, 00, 30, TimeSpan.Zero);

    private readonly IntegrationTestFixture _fixture;

    public PendingInvoiceProjectionTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Example_1_1_OrderConfirmed_Then_PaymentCaptured_ConvergesPendingRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();

        var orderClock = new FakeTimeProvider(OrderArrivalUtc);
        var paymentClock = new FakeTimeProvider(PaymentArrivalUtc);

        // Order half arrives first.
        await using (var orderScope = _fixture.CreateScope())
        {
            var db = orderScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var orderHandler = new OrderConfirmedInvoiceProjectionKafkaHandler(
                db, orderClock, NullLogger<OrderConfirmedInvoiceProjectionKafkaHandler>.Instance);

            await orderHandler.Handle(
                BuildContext(ct),
                new AvroOrderConfirmedEvent
                {
                    OrderId = orderId,
                    CorrelationId = correlationId,
                    BuyerId = buyerId,
                    ConfirmedAtUtc = OrderArrivalUtc.UtcDateTime,
                });
        }

        // Verify intermediate state: order half captured, payment half still null, NOT converged.
        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var midRow = await db.PendingInvoices.AsNoTracking()
                .SingleAsync(r => r.CorrelationId == correlationId, ct);

            using var _ = new AssertionScope();
            midRow.OrderId.Should().Be(orderId);
            midRow.BuyerId.Should().Be(buyerId);
            midRow.OrderPayload.Should().NotBeNullOrEmpty();
            midRow.PaymentId.Should().BeNull();
            midRow.PaymentPayload.Should().BeNull();
            midRow.FirstSeenAtUtc.Should().Be(OrderArrivalUtc);
            midRow.CompletedAtUtc.Should().BeNull("payment half has not arrived");
            midRow.IssuedInvoiceId.Should().BeNull("M7 owns issuance");
        }

        // Payment half arrives second — converges the row.
        await using (var paymentScope = _fixture.CreateScope())
        {
            var db = paymentScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var paymentHandler = new PaymentCapturedInvoiceProjectionKafkaHandler(
                db, paymentClock, NullLogger<PaymentCapturedInvoiceProjectionKafkaHandler>.Instance);

            await paymentHandler.Handle(
                BuildContext(ct),
                BuildPaymentCapturedEvent(correlationId, paymentTransactionId, buyerId));
        }

        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var converged = await db.PendingInvoices.AsNoTracking()
                .SingleAsync(r => r.CorrelationId == correlationId, ct);

            using var _ = new AssertionScope();
            converged.OrderId.Should().Be(orderId);
            converged.PaymentId.Should().Be(paymentTransactionId);
            converged.OrderPayload.Should().NotBeNullOrEmpty();
            converged.PaymentPayload.Should().NotBeNullOrEmpty();
            converged.FirstSeenAtUtc.Should().Be(OrderArrivalUtc, "first-seen never overwrites");
            converged.CompletedAtUtc.Should().Be(PaymentArrivalUtc);
            converged.IssuedInvoiceId.Should().BeNull("M7 owns issuance");
        }
    }

    [Fact]
    public async Task Example_1_2_PaymentCaptured_Then_OrderConfirmed_ConvergesPendingRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();

        // Payment half arrives FIRST.
        var paymentClock = new FakeTimeProvider(PaymentArrivalUtc);
        var orderClock = new FakeTimeProvider(PaymentArrivalUtc.AddSeconds(45));

        await using (var paymentScope = _fixture.CreateScope())
        {
            var db = paymentScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var paymentHandler = new PaymentCapturedInvoiceProjectionKafkaHandler(
                db, paymentClock, NullLogger<PaymentCapturedInvoiceProjectionKafkaHandler>.Instance);

            await paymentHandler.Handle(
                BuildContext(ct),
                BuildPaymentCapturedEvent(correlationId, paymentTransactionId, buyerId));
        }

        // Verify intermediate state: payment captured, OrderId still null.
        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var midRow = await db.PendingInvoices.AsNoTracking()
                .SingleAsync(r => r.CorrelationId == correlationId, ct);

            using var _ = new AssertionScope();
            midRow.PaymentId.Should().Be(paymentTransactionId);
            midRow.PaymentPayload.Should().NotBeNullOrEmpty();
            midRow.OrderId.Should().BeNull();
            midRow.OrderPayload.Should().BeNull();
            midRow.BuyerId.Should().BeNull("buyer comes from the order half");
            midRow.FirstSeenAtUtc.Should().Be(PaymentArrivalUtc);
            midRow.CompletedAtUtc.Should().BeNull();
        }

        // Order half arrives second — converges.
        await using (var orderScope = _fixture.CreateScope())
        {
            var db = orderScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var orderHandler = new OrderConfirmedInvoiceProjectionKafkaHandler(
                db, orderClock, NullLogger<OrderConfirmedInvoiceProjectionKafkaHandler>.Instance);

            await orderHandler.Handle(
                BuildContext(ct),
                new AvroOrderConfirmedEvent
                {
                    OrderId = orderId,
                    CorrelationId = correlationId,
                    BuyerId = buyerId,
                    ConfirmedAtUtc = orderClock.GetUtcNow().UtcDateTime,
                });
        }

        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var converged = await db.PendingInvoices.AsNoTracking()
                .SingleAsync(r => r.CorrelationId == correlationId, ct);

            using var _ = new AssertionScope();
            converged.OrderId.Should().Be(orderId);
            converged.PaymentId.Should().Be(paymentTransactionId);
            converged.BuyerId.Should().Be(buyerId);
            converged.OrderPayload.Should().NotBeNullOrEmpty();
            converged.PaymentPayload.Should().NotBeNullOrEmpty();
            converged.FirstSeenAtUtc.Should().Be(PaymentArrivalUtc, "payment was first");
            converged.CompletedAtUtc.Should().Be(orderClock.GetUtcNow());
            converged.IssuedInvoiceId.Should().BeNull();
        }
    }

    [Fact]
    public async Task Example_1_3_DuplicateOrderConfirmedEvent_RowStaysIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();

        var firstClock = new FakeTimeProvider(OrderArrivalUtc);
        var secondClock = new FakeTimeProvider(OrderArrivalUtc.AddMinutes(2));

        // First arrival inserts.
        await using (var firstScope = _fixture.CreateScope())
        {
            var db = firstScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var handler = new OrderConfirmedInvoiceProjectionKafkaHandler(
                db, firstClock, NullLogger<OrderConfirmedInvoiceProjectionKafkaHandler>.Instance);

            await handler.Handle(
                BuildContext(ct),
                new AvroOrderConfirmedEvent
                {
                    OrderId = orderId,
                    CorrelationId = correlationId,
                    BuyerId = buyerId,
                    ConfirmedAtUtc = OrderArrivalUtc.UtcDateTime,
                });
        }

        // Same CorrelationId redelivered — handler must no-op.
        await using (var secondScope = _fixture.CreateScope())
        {
            var db = secondScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var handler = new OrderConfirmedInvoiceProjectionKafkaHandler(
                db, secondClock, NullLogger<OrderConfirmedInvoiceProjectionKafkaHandler>.Instance);

            await handler.Handle(
                BuildContext(ct),
                new AvroOrderConfirmedEvent
                {
                    OrderId = orderId,
                    CorrelationId = correlationId,
                    BuyerId = buyerId,
                    ConfirmedAtUtc = OrderArrivalUtc.UtcDateTime,
                });
        }

        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();

            var rows = await db.PendingInvoices.AsNoTracking()
                .Where(r => r.CorrelationId == correlationId)
                .ToListAsync(ct);

            using var _ = new AssertionScope();
            rows.Should().HaveCount(1, "duplicate same-CorrelationId arrival is absorbed");
            rows[0].FirstSeenAtUtc.Should().Be(OrderArrivalUtc, "FirstSeenAtUtc is never overwritten");
            rows[0].PaymentId.Should().BeNull("payment half never arrived");
            rows[0].CompletedAtUtc.Should().BeNull();
        }
    }

    private static IMessageContext BuildContext(CancellationToken ct)
    {
        var context = Substitute.For<IMessageContext>();
        context.Headers.Returns(new MessageHeaders());
        var consumerContext = Substitute.For<IConsumerContext>();
        consumerContext.WorkerStopped.Returns(ct);
        context.ConsumerContext.Returns(consumerContext);
        return context;
    }

    private static AvroPaymentCapturedEvent BuildPaymentCapturedEvent(
        Guid correlationId, Guid paymentTransactionId, Guid buyerId)
    {
        return new AvroPaymentCapturedEvent
        {
            CorrelationId = correlationId,
            UserId = buyerId,
            PaymentTransactionId = paymentTransactionId,
            AuthorizationId = "auth-test",
            Amount = new Avro.AvroDecimal(152.00m),
            Currency = "EUR",
            CapturedAtUtc = PaymentArrivalUtc.UtcDateTime,
        };
    }
}
