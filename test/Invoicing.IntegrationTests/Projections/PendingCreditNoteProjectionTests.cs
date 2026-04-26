using AwesomeAssertions;
using Invoicing.Application.CreditNotes.Projections;
using Invoicing.Infrastructure.Messaging.Kafka.Projections;
using Invoicing.Infrastructure.Persistence.Database;
using Invoicing.IntegrationTests.Common;
using KafkaFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using AvroOrderCancelledEvent = Ordering.Orders.OrderCancelledEvent;
using AvroOrderStatusAtTransition = Ordering.Orders.OrderStatusAtTransition;
using AvroPaymentRefundedEvent = Payments.Transactions.PaymentRefundedEvent;

namespace Invoicing.IntegrationTests.Projections;

/// <summary>
/// Mirrors <see cref="PendingInvoiceProjectionTests"/> for the credit-note
/// convergence pair: <c>OrderCancelledEvent</c> + <c>PaymentRefundedEvent</c>.
/// Asserts the same three guarantees per <c>example-mapping/invoicing.md § 3</c>:
/// order-cancel-first, refund-first, duplicate idempotency.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class PendingCreditNoteProjectionTests
{
    private static readonly DateTimeOffset CancelArrivalUtc =
        new(2026, 04, 26, 11, 00, 00, TimeSpan.Zero);

    private static readonly DateTimeOffset RefundArrivalUtc =
        new(2026, 04, 26, 11, 00, 30, TimeSpan.Zero);

    private readonly IntegrationTestFixture _fixture;

    public PendingCreditNoteProjectionTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OrderCancelled_Then_PaymentRefunded_ConvergesPendingCreditNote()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();

        var cancelClock = new FakeTimeProvider(CancelArrivalUtc);
        var refundClock = new FakeTimeProvider(RefundArrivalUtc);

        await using (var cancelScope = _fixture.CreateScope())
        {
            var db = cancelScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var cancelHandler = new OrderCancelledCreditNoteProjectionKafkaHandler(
                db, cancelClock, NullLogger<OrderCancelledCreditNoteProjectionKafkaHandler>.Instance);

            await cancelHandler.Handle(
                BuildContext(ct),
                BuildOrderCancelledEvent(correlationId, orderId, buyerId, CancelArrivalUtc));
        }

        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var midRow = await db.PendingCreditNotes.AsNoTracking()
                .SingleAsync(r => r.CorrelationId == correlationId, ct);

            using var _ = new AssertionScope();
            midRow.OrderId.Should().Be(orderId);
            midRow.BuyerId.Should().Be(buyerId);
            midRow.OrderPayload.Should().NotBeNullOrEmpty();
            midRow.PaymentId.Should().BeNull();
            midRow.CompletedAtUtc.Should().BeNull();
            midRow.IssuedCreditNoteId.Should().BeNull();
        }

        await using (var refundScope = _fixture.CreateScope())
        {
            var db = refundScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var refundHandler = new PaymentRefundedCreditNoteProjectionKafkaHandler(
                db, refundClock, NullLogger<PaymentRefundedCreditNoteProjectionKafkaHandler>.Instance);

            await refundHandler.Handle(
                BuildContext(ct),
                BuildPaymentRefundedEvent(correlationId, paymentTransactionId, buyerId));
        }

        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var converged = await db.PendingCreditNotes.AsNoTracking()
                .SingleAsync(r => r.CorrelationId == correlationId, ct);

            using var _ = new AssertionScope();
            converged.OrderId.Should().Be(orderId);
            converged.PaymentId.Should().Be(paymentTransactionId);
            converged.OrderPayload.Should().NotBeNullOrEmpty();
            converged.PaymentPayload.Should().NotBeNullOrEmpty();
            converged.FirstSeenAtUtc.Should().Be(CancelArrivalUtc);
            converged.CompletedAtUtc.Should().Be(RefundArrivalUtc);
            converged.IssuedCreditNoteId.Should().BeNull();
        }
    }

    [Fact]
    public async Task PaymentRefunded_Then_OrderCancelled_ConvergesPendingCreditNote()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();

        var refundClock = new FakeTimeProvider(RefundArrivalUtc);
        var cancelClock = new FakeTimeProvider(RefundArrivalUtc.AddSeconds(45));

        await using (var refundScope = _fixture.CreateScope())
        {
            var db = refundScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var refundHandler = new PaymentRefundedCreditNoteProjectionKafkaHandler(
                db, refundClock, NullLogger<PaymentRefundedCreditNoteProjectionKafkaHandler>.Instance);

            await refundHandler.Handle(
                BuildContext(ct),
                BuildPaymentRefundedEvent(correlationId, paymentTransactionId, buyerId));
        }

        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var midRow = await db.PendingCreditNotes.AsNoTracking()
                .SingleAsync(r => r.CorrelationId == correlationId, ct);

            using var _ = new AssertionScope();
            midRow.PaymentId.Should().Be(paymentTransactionId);
            midRow.PaymentPayload.Should().NotBeNullOrEmpty();
            midRow.OrderId.Should().BeNull();
            midRow.OrderPayload.Should().BeNull();
            midRow.BuyerId.Should().BeNull("buyer comes from the order-cancel half");
            midRow.FirstSeenAtUtc.Should().Be(RefundArrivalUtc);
            midRow.CompletedAtUtc.Should().BeNull();
        }

        await using (var cancelScope = _fixture.CreateScope())
        {
            var db = cancelScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var cancelHandler = new OrderCancelledCreditNoteProjectionKafkaHandler(
                db, cancelClock, NullLogger<OrderCancelledCreditNoteProjectionKafkaHandler>.Instance);

            await cancelHandler.Handle(
                BuildContext(ct),
                BuildOrderCancelledEvent(correlationId, orderId, buyerId, cancelClock.GetUtcNow()));
        }

        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var converged = await db.PendingCreditNotes.AsNoTracking()
                .SingleAsync(r => r.CorrelationId == correlationId, ct);

            using var _ = new AssertionScope();
            converged.OrderId.Should().Be(orderId);
            converged.PaymentId.Should().Be(paymentTransactionId);
            converged.BuyerId.Should().Be(buyerId);
            converged.FirstSeenAtUtc.Should().Be(RefundArrivalUtc);
            converged.CompletedAtUtc.Should().Be(cancelClock.GetUtcNow());
            converged.IssuedCreditNoteId.Should().BeNull();
        }
    }

    [Fact]
    public async Task DuplicatePaymentRefundedEvent_RowStaysIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();

        var firstClock = new FakeTimeProvider(RefundArrivalUtc);
        var secondClock = new FakeTimeProvider(RefundArrivalUtc.AddMinutes(2));

        await using (var firstScope = _fixture.CreateScope())
        {
            var db = firstScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var handler = new PaymentRefundedCreditNoteProjectionKafkaHandler(
                db, firstClock, NullLogger<PaymentRefundedCreditNoteProjectionKafkaHandler>.Instance);

            await handler.Handle(
                BuildContext(ct),
                BuildPaymentRefundedEvent(correlationId, paymentTransactionId, buyerId));
        }

        await using (var secondScope = _fixture.CreateScope())
        {
            var db = secondScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var handler = new PaymentRefundedCreditNoteProjectionKafkaHandler(
                db, secondClock, NullLogger<PaymentRefundedCreditNoteProjectionKafkaHandler>.Instance);

            await handler.Handle(
                BuildContext(ct),
                BuildPaymentRefundedEvent(correlationId, paymentTransactionId, buyerId));
        }

        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();

            var rows = await db.PendingCreditNotes.AsNoTracking()
                .Where(r => r.CorrelationId == correlationId)
                .ToListAsync(ct);

            using var _ = new AssertionScope();
            rows.Should().HaveCount(1);
            rows[0].FirstSeenAtUtc.Should().Be(RefundArrivalUtc, "FirstSeenAtUtc is never overwritten");
            rows[0].OrderId.Should().BeNull();
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

    private static AvroOrderCancelledEvent BuildOrderCancelledEvent(
        Guid correlationId, Guid orderId, Guid buyerId, DateTimeOffset cancelledAt)
    {
        return new AvroOrderCancelledEvent
        {
            OrderId = orderId,
            CorrelationId = correlationId,
            BuyerId = buyerId,
            Reason = "Customer requested",
            AtStatus = AvroOrderStatusAtTransition.Confirmed,
            CancelledAtUtc = cancelledAt.UtcDateTime,
        };
    }

    private static AvroPaymentRefundedEvent BuildPaymentRefundedEvent(
        Guid correlationId, Guid paymentTransactionId, Guid buyerId)
    {
        return new AvroPaymentRefundedEvent
        {
            CorrelationId = correlationId,
            UserId = buyerId,
            PaymentTransactionId = paymentTransactionId,
            RefundTransactionId = Guid.CreateVersion7(),
            RefundedAmount = new Avro.AvroDecimal(152.00m),
            Currency = "EUR",
            RefundedAtUtc = RefundArrivalUtc.UtcDateTime,
        };
    }
}
