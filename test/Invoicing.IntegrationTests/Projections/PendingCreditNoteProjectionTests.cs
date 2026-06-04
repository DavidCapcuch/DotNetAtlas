using System.Text.Json;
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
using Platform.Messaging.Abstractions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using AvroOrderCancellationBillingAddress = Ordering.Orders.OrderCancellationBillingAddress;
using AvroOrderCancelledEvent = Ordering.Orders.OrderCancelledEvent;
using AvroOrderItemCancelled = Ordering.Orders.OrderItemCancelled;
using AvroOrderStatusAtTransition = Ordering.Orders.OrderStatusAtTransition;
using AvroPaymentRefundedEvent = Payments.Transactions.PaymentRefundedEvent;

namespace Invoicing.IntegrationTests.Projections;

/// <summary>
/// Mirrors <see cref="PendingInvoiceProjectionTests"/> for the credit-note
/// convergence pair: <c>OrderCancelledEvent</c> + <c>PaymentRefundedEvent</c>.
/// Asserts the same three guarantees per <c>example-mapping/invoicing.md § 3</c>:
/// order-cancel-first, refund-first, duplicate idempotency.
/// </summary>
/// <remarks>
/// As of Wave 1.6 / ADR-0020 the consumed Avro <c>OrderCancelledEvent</c> is
/// a Summary Event — Items, TotalAmount, Currency, BillingAddress all travel
/// with it and are persisted into <c>pending_credit_notes.OrderPayload</c>
/// to read. Each test asserts the round-trip through the jsonb column.
/// </remarks>
[Collection<IntegrationTestCollection>]
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

        var cancelEvent = BuildOrderCancelledEvent(orderId, buyerId, CancelArrivalUtc);

        await using (var cancelScope = _fixture.CreateScope())
        {
            var db = cancelScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var cancelHandler = new OrderCancelledCreditNoteProjectionKafkaHandler(
                db,
                M7CommandHandlerStubs.NoOpIssueCreditNoteHandler(),
                cancelClock,
                NullLogger<OrderCancelledCreditNoteProjectionKafkaHandler>.Instance);

            await cancelHandler.Handle(BuildContext(correlationId, ct), cancelEvent);
        }

        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var midRow = await db.PendingCreditNotes.AsNoTracking()
                .SingleAsync(r => r.OrderId == orderId, ct);

            using var _ = new AssertionScope();
            midRow.OrderId.Should().Be(orderId);
            midRow.BuyerId.Should().Be(buyerId);
            midRow.OrderPayload.Should().NotBeNullOrEmpty();
            AssertOrderPayloadMatches(midRow.OrderPayload!, cancelEvent);
            midRow.PaymentId.Should().BeNull();
            midRow.CompletedAtUtc.Should().BeNull();
            midRow.IssuedCreditNoteId.Should().BeNull();
        }

        await using (var refundScope = _fixture.CreateScope())
        {
            var db = refundScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var refundHandler = new PaymentRefundedCreditNoteProjectionKafkaHandler(
                db,
                M7CommandHandlerStubs.NoOpIssueCreditNoteHandler(),
                refundClock,
                NullLogger<PaymentRefundedCreditNoteProjectionKafkaHandler>.Instance);

            await refundHandler.Handle(
                BuildContext(correlationId, ct),
                BuildPaymentRefundedEvent(orderId, paymentTransactionId, buyerId));
        }

        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var converged = await db.PendingCreditNotes.AsNoTracking()
                .SingleAsync(r => r.OrderId == orderId, ct);

            using var _ = new AssertionScope();
            converged.OrderId.Should().Be(orderId);
            converged.PaymentId.Should().Be(paymentTransactionId);
            converged.OrderPayload.Should().NotBeNullOrEmpty();
            AssertOrderPayloadMatches(converged.OrderPayload!, cancelEvent);
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
                db,
                M7CommandHandlerStubs.NoOpIssueCreditNoteHandler(),
                refundClock,
                NullLogger<PaymentRefundedCreditNoteProjectionKafkaHandler>.Instance);

            await refundHandler.Handle(
                BuildContext(correlationId, ct),
                BuildPaymentRefundedEvent(orderId, paymentTransactionId, buyerId));
        }

        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var midRow = await db.PendingCreditNotes.AsNoTracking()
                .SingleAsync(r => r.OrderId == orderId, ct);

            using var _ = new AssertionScope();
            midRow.PaymentId.Should().Be(paymentTransactionId);
            midRow.PaymentPayload.Should().NotBeNullOrEmpty();
            // OrderId is the PK — set by whichever half arrives first (here, the refund half
            // carries it post-ADR-0029). The "order-cancel half not yet seen" sentinel is OrderPayload.
            midRow.OrderId.Should().Be(orderId);
            midRow.OrderPayload.Should().BeNull();
            midRow.BuyerId.Should().BeNull("buyer comes from the order-cancel half");
            midRow.FirstSeenAtUtc.Should().Be(RefundArrivalUtc);
            midRow.CompletedAtUtc.Should().BeNull();
        }

        var cancelEvent = BuildOrderCancelledEvent(
            orderId, buyerId, cancelClock.GetUtcNow());
        await using (var cancelScope = _fixture.CreateScope())
        {
            var db = cancelScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var cancelHandler = new OrderCancelledCreditNoteProjectionKafkaHandler(
                db,
                M7CommandHandlerStubs.NoOpIssueCreditNoteHandler(),
                cancelClock,
                NullLogger<OrderCancelledCreditNoteProjectionKafkaHandler>.Instance);

            await cancelHandler.Handle(BuildContext(correlationId, ct), cancelEvent);
        }

        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var converged = await db.PendingCreditNotes.AsNoTracking()
                .SingleAsync(r => r.OrderId == orderId, ct);

            using var _ = new AssertionScope();
            converged.OrderId.Should().Be(orderId);
            converged.PaymentId.Should().Be(paymentTransactionId);
            converged.BuyerId.Should().Be(buyerId);
            converged.OrderPayload.Should().NotBeNullOrEmpty();
            AssertOrderPayloadMatches(converged.OrderPayload!, cancelEvent);
            converged.FirstSeenAtUtc.Should().Be(RefundArrivalUtc);
            converged.CompletedAtUtc.Should().Be(cancelClock.GetUtcNow());
            converged.IssuedCreditNoteId.Should().BeNull("M7 stub does not mutate IssuedCreditNoteId");
        }
    }

    [Fact]
    public async Task DuplicatePaymentRefundedEvent_RowStaysIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();

        var firstClock = new FakeTimeProvider(RefundArrivalUtc);
        var secondClock = new FakeTimeProvider(RefundArrivalUtc.AddMinutes(2));

        await using (var firstScope = _fixture.CreateScope())
        {
            var db = firstScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var handler = new PaymentRefundedCreditNoteProjectionKafkaHandler(
                db,
                M7CommandHandlerStubs.NoOpIssueCreditNoteHandler(),
                firstClock,
                NullLogger<PaymentRefundedCreditNoteProjectionKafkaHandler>.Instance);

            await handler.Handle(
                BuildContext(correlationId, ct),
                BuildPaymentRefundedEvent(orderId, paymentTransactionId, buyerId));
        }

        await using (var secondScope = _fixture.CreateScope())
        {
            var db = secondScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var handler = new PaymentRefundedCreditNoteProjectionKafkaHandler(
                db,
                M7CommandHandlerStubs.NoOpIssueCreditNoteHandler(),
                secondClock,
                NullLogger<PaymentRefundedCreditNoteProjectionKafkaHandler>.Instance);

            await handler.Handle(
                BuildContext(correlationId, ct),
                BuildPaymentRefundedEvent(orderId, paymentTransactionId, buyerId));
        }

        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();

            var rows = await db.PendingCreditNotes.AsNoTracking()
                .Where(r => r.OrderId == orderId)
                .ToListAsync(ct);

            using var _ = new AssertionScope();
            rows.Should().HaveCount(1);
            rows[0].FirstSeenAtUtc.Should().Be(RefundArrivalUtc, "FirstSeenAtUtc is never overwritten");
            // OrderId is the PK (the refund half carries it); order-cancel sentinel is OrderPayload.
            rows[0].OrderId.Should().Be(orderId);
            rows[0].OrderPayload.Should().BeNull("order-cancel half never arrived");
            rows[0].CompletedAtUtc.Should().BeNull();
        }
    }

    [Fact]
    public async Task DuplicateOrderCancelledEvent_RowStaysIdempotent_FirstArrivalWins()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();

        var firstClock = new FakeTimeProvider(CancelArrivalUtc);
        var secondClock = new FakeTimeProvider(CancelArrivalUtc.AddMinutes(2));

        var firstEvent = BuildOrderCancelledEvent(orderId, buyerId, CancelArrivalUtc);
        // Second arrival deliberately differs so the assertion proves the row
        // keeps the FIRST payload — locks in ADR-0020 / Wave 1.6 contract:
        // first-arrival wins, second arrival never overwrites OrderPayload.
        var secondEvent = BuildOrderCancelledEvent(
            orderId, buyerId, CancelArrivalUtc, totalOverride: 999.99m);

        await using (var firstScope = _fixture.CreateScope())
        {
            var db = firstScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var handler = new OrderCancelledCreditNoteProjectionKafkaHandler(
                db,
                M7CommandHandlerStubs.NoOpIssueCreditNoteHandler(),
                firstClock,
                NullLogger<OrderCancelledCreditNoteProjectionKafkaHandler>.Instance);

            await handler.Handle(BuildContext(correlationId, ct), firstEvent);
        }

        await using (var secondScope = _fixture.CreateScope())
        {
            var db = secondScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var handler = new OrderCancelledCreditNoteProjectionKafkaHandler(
                db,
                M7CommandHandlerStubs.NoOpIssueCreditNoteHandler(),
                secondClock,
                NullLogger<OrderCancelledCreditNoteProjectionKafkaHandler>.Instance);

            await handler.Handle(BuildContext(correlationId, ct), secondEvent);
        }

        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();

            var rows = await db.PendingCreditNotes.AsNoTracking()
                .Where(r => r.OrderId == orderId)
                .ToListAsync(ct);

            using var _ = new AssertionScope();
            rows.Should().HaveCount(1, "duplicate same-OrderId arrival is absorbed");
            rows[0].FirstSeenAtUtc.Should().Be(CancelArrivalUtc, "FirstSeenAtUtc is never overwritten");
            rows[0].PaymentId.Should().BeNull("refund half never arrived");
            rows[0].CompletedAtUtc.Should().BeNull();
            AssertOrderPayloadMatches(rows[0].OrderPayload!, firstEvent);
        }
    }

    private static IMessageContext BuildContext(Guid correlationId, CancellationToken ct)
    {
        var context = Substitute.For<IMessageContext>();
        // ADR-0008 — projection handlers source CorrelationId from this header; tests that
        // assert on a specific value flowing through must pass it here so header == Avro payload.
        context.Headers.Returns(new MessageHeaders
        {
            {
                MessageHeaderKeys.CorrelationId,
                System.Text.Encoding.UTF8.GetBytes(correlationId.ToString())
            },
        });
        var consumerContext = Substitute.For<IConsumerContext>();
        consumerContext.WorkerStopped.Returns(ct);
        context.ConsumerContext.Returns(consumerContext);
        return context;
    }

    private static AvroOrderCancelledEvent BuildOrderCancelledEvent(
        Guid orderId,
        Guid buyerId,
        DateTimeOffset cancelledAt,
        decimal? totalOverride = null)
    {
        // Use the production scale-4 helper so the test exercises the same
        // wire path as Ordering's OrderCancelledMapper. Avoid the bare
        // AvroDecimal(decimal) ctor — it preserves the .NET decimal's native
        // scale and would never trip a scale-mismatch bug in the projection.
        const int Scale = 4;
        var lineTotal = totalOverride ?? 152.00m;
        return new AvroOrderCancelledEvent
        {
            OrderId = orderId,
            BuyerId = buyerId,
            Reason = "Customer requested",
            AtStatus = AvroOrderStatusAtTransition.Confirmed,
            CancelledAtUtc = cancelledAt.UtcDateTime,
            Items =
            [
                new AvroOrderItemCancelled
                {
                    ProductId = Guid.CreateVersion7(),
                    Sku = "SKU-1",
                    Name = "Test Product",
                    Quantity = 1,
                    UnitPriceAmount = lineTotal.ToAvroDecimal(Scale),
                    LineTotalAmount = lineTotal.ToAvroDecimal(Scale),
                },
            ],
            TotalAmount = lineTotal.ToAvroDecimal(Scale),
            Currency = "EUR",
            BillingAddress = new AvroOrderCancellationBillingAddress
            {
                Street1 = "1 Main St",
                Street2 = null,
                City = "Prague",
                State = null,
                PostalCode = "11000",
                CountryCode = "CZ",
            },
        };
    }

    private static AvroPaymentRefundedEvent BuildPaymentRefundedEvent(
        Guid orderId, Guid paymentTransactionId, Guid buyerId)
    {
        return new AvroPaymentRefundedEvent
        {
            OrderId = orderId,
            UserId = buyerId,
            PaymentTransactionId = paymentTransactionId,
            RefundTransactionId = Guid.CreateVersion7(),
            RefundedAmount = new Avro.AvroDecimal(152.00m),
            Currency = "EUR",
            RefundedAtUtc = RefundArrivalUtc.UtcDateTime,
        };
    }

    private static void AssertOrderPayloadMatches(string orderPayloadJson, AvroOrderCancelledEvent expected)
    {
        var dto = JsonSerializer.Deserialize<OrderPayloadDto>(orderPayloadJson)
                  ?? throw new InvalidOperationException("OrderPayload failed to deserialise.");

        dto.OrderId.Should().Be(expected.OrderId);
        dto.BuyerId.Should().Be(expected.BuyerId);
        dto.Reason.Should().Be(expected.Reason);
        dto.AtStatus.Should().Be(expected.AtStatus.ToString());
        dto.Currency.Should().Be(expected.Currency);
        dto.TotalAmount.Should().Be((decimal)expected.TotalAmount!.Value);

        dto.Items.Should().NotBeNull();
        dto.Items!.Should().HaveCount(expected.Items.Count);
        var firstActual = dto.Items[0];
        var firstExpected = expected.Items[0];
        firstActual.ProductId.Should().Be(firstExpected.ProductId);
        firstActual.Sku.Should().Be(firstExpected.Sku);
        firstActual.Name.Should().Be(firstExpected.Name);
        firstActual.Quantity.Should().Be(firstExpected.Quantity);
        firstActual.UnitPriceAmount.Should().Be((decimal)firstExpected.UnitPriceAmount);
        firstActual.LineTotalAmount.Should().Be((decimal)firstExpected.LineTotalAmount);

        dto.BillingAddress.Should().NotBeNull();
        dto.BillingAddress!.Street1.Should().Be(expected.BillingAddress.Street1);
        dto.BillingAddress.Street2.Should().Be(expected.BillingAddress.Street2);
        dto.BillingAddress.City.Should().Be(expected.BillingAddress.City);
        dto.BillingAddress.State.Should().Be(expected.BillingAddress.State);
        dto.BillingAddress.PostalCode.Should().Be(expected.BillingAddress.PostalCode);
        dto.BillingAddress.CountryCode.Should().Be(expected.BillingAddress.CountryCode);
    }

    /// <summary>
    /// Mirror of the anonymous DTO emitted by
    /// <see cref="OrderCancelledCreditNoteProjectionKafkaHandler.SerializePayload"/>.
    /// Lives here (not in production code) because will introduce its own
    /// strongly-typed reader; this test-only DTO documents the wire contract
    /// the projection currently produces.
    /// </summary>
    private sealed record OrderPayloadDto(
        Guid OrderId,
        Guid BuyerId,
        string Reason,
        string AtStatus,
        DateTime CancelledAtUtc,
        IReadOnlyList<OrderPayloadItemDto>? Items,
        decimal? TotalAmount,
        string? Currency,
        OrderPayloadAddressDto? BillingAddress);

    private sealed record OrderPayloadItemDto(
        Guid ProductId,
        string Sku,
        string Name,
        int Quantity,
        decimal UnitPriceAmount,
        decimal LineTotalAmount);

    private sealed record OrderPayloadAddressDto(
        string Street1,
        string? Street2,
        string City,
        string? State,
        string PostalCode,
        string CountryCode);
}
