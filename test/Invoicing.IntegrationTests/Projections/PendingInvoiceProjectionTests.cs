using System.Text.Json;
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
using AvroOrderBillingAddress = Ordering.Orders.OrderBillingAddress;
using AvroOrderConfirmedEvent = Ordering.Orders.OrderConfirmedEvent;
using AvroOrderItemConfirmed = Ordering.Orders.OrderItemConfirmed;
using AvroPaymentCapturedEvent = Payments.Transactions.PaymentCapturedEvent;

namespace Invoicing.IntegrationTests.Projections;

/// <summary>
/// Integration tests for the invoice-side enrichment projection. Cover the
/// three convergence sessions from <c>example-mapping/invoicing.md § 1</c>:
/// order-first (1.1), payment-first (1.2), duplicate-order idempotency (1.3).
/// Handlers are exercised directly with an NSubstitute <c>IMessageContext</c>;
/// the inbox middleware that fronts the consumers in production is covered
/// by Platform.KafkaFlow.Inbox.EFCore's own tests.
/// </summary>
/// <remarks>
/// As of Wave 1.5 / ADR-0020 the consumed Avro <c>OrderConfirmedEvent</c> is
/// a Summary Event — Items, TotalAmount, Currency, BillingAddress all travel
/// with it and are persisted into <c>pending_invoices.OrderPayload</c> for
/// to read. Each test asserts the round-trip through the jsonb column.
/// </remarks>
[Collection<IntegrationTestCollection>]
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
        var orderId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();

        var orderClock = new FakeTimeProvider(OrderArrivalUtc);
        var paymentClock = new FakeTimeProvider(PaymentArrivalUtc);

        var orderEvent = BuildOrderConfirmedEvent(orderId, buyerId);

        // Order half arrives first.
        await using (var orderScope = _fixture.CreateScope())
        {
            var db = orderScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var orderHandler = new OrderConfirmedInvoiceProjectionKafkaHandler(
                db,
                M7CommandHandlerStubs.NoOpIssueInvoiceHandler(),
                orderClock,
                NullLogger<OrderConfirmedInvoiceProjectionKafkaHandler>.Instance);

            await orderHandler.Handle(BuildContext(ct), orderEvent);
        }

        // Verify intermediate state: order half captured, payment half still null, NOT converged.
        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var midRow = await db.PendingInvoices.AsNoTracking()
                .SingleAsync(r => r.OrderId == orderId, ct);

            using (new AssertionScope())
            {
                midRow.OrderId.Should().Be(orderId);
                midRow.BuyerId.Should().Be(buyerId);
                midRow.OrderPayload.Should().NotBeNullOrEmpty();
                AssertOrderPayloadMatches(midRow.OrderPayload!, orderEvent);
                midRow.PaymentId.Should().BeNull();
                midRow.PaymentPayload.Should().BeNull();
                midRow.FirstSeenAtUtc.Should().Be(OrderArrivalUtc);
                midRow.CompletedAtUtc.Should().BeNull("payment half has not arrived");
                midRow.IssuedInvoiceId.Should().BeNull("M7 owns issuance");
            }
        }

        // Payment half arrives second — converges the row.
        await using (var paymentScope = _fixture.CreateScope())
        {
            var db = paymentScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var paymentHandler = new PaymentCapturedInvoiceProjectionKafkaHandler(
                db,
                M7CommandHandlerStubs.NoOpIssueInvoiceHandler(),
                paymentClock,
                NullLogger<PaymentCapturedInvoiceProjectionKafkaHandler>.Instance);

            await paymentHandler.Handle(
                BuildContext(ct),
                BuildPaymentCapturedEvent(orderId, paymentTransactionId, buyerId));
        }

        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var converged = await db.PendingInvoices.AsNoTracking()
                .SingleAsync(r => r.OrderId == orderId, ct);

            using (new AssertionScope())
            {
                converged.OrderId.Should().Be(orderId);
                converged.PaymentId.Should().Be(paymentTransactionId);
                converged.OrderPayload.Should().NotBeNullOrEmpty();
                AssertOrderPayloadMatches(converged.OrderPayload!, orderEvent);
                converged.PaymentPayload.Should().NotBeNullOrEmpty();
                converged.FirstSeenAtUtc.Should().Be(OrderArrivalUtc, "first-seen never overwrites");
                converged.CompletedAtUtc.Should().Be(PaymentArrivalUtc);
                converged.IssuedInvoiceId.Should().BeNull("M7 owns issuance");
            }
        }
    }

    [Fact]
    public async Task Example_1_2_PaymentCaptured_Then_OrderConfirmed_ConvergesPendingRow()
    {
        var ct = TestContext.Current.CancellationToken;
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
                db,
                M7CommandHandlerStubs.NoOpIssueInvoiceHandler(),
                paymentClock,
                NullLogger<PaymentCapturedInvoiceProjectionKafkaHandler>.Instance);

            await paymentHandler.Handle(
                BuildContext(ct),
                BuildPaymentCapturedEvent(orderId, paymentTransactionId, buyerId));
        }

        // Verify intermediate state: payment captured, order-cancel half not yet seen.
        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var midRow = await db.PendingInvoices.AsNoTracking()
                .SingleAsync(r => r.OrderId == orderId, ct);

            using (new AssertionScope())
            {
                midRow.PaymentId.Should().Be(paymentTransactionId);
                midRow.PaymentPayload.Should().NotBeNullOrEmpty();
                // OrderId is the PK — set by whichever half arrives first (here, the payment half
                // carries it post-ADR-0029). The "order half not yet seen" sentinel is OrderPayload.
                midRow.OrderId.Should().Be(orderId);
                midRow.OrderPayload.Should().BeNull();
                midRow.BuyerId.Should().BeNull("buyer comes from the order half");
                midRow.FirstSeenAtUtc.Should().Be(PaymentArrivalUtc);
                midRow.CompletedAtUtc.Should().BeNull();
            }
        }

        // Order half arrives second — converges.
        var orderEvent = BuildOrderConfirmedEvent(
            orderId, buyerId, confirmedAtUtc: orderClock.GetUtcNow().UtcDateTime);
        await using (var orderScope = _fixture.CreateScope())
        {
            var db = orderScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var orderHandler = new OrderConfirmedInvoiceProjectionKafkaHandler(
                db,
                M7CommandHandlerStubs.NoOpIssueInvoiceHandler(),
                orderClock,
                NullLogger<OrderConfirmedInvoiceProjectionKafkaHandler>.Instance);

            await orderHandler.Handle(BuildContext(ct), orderEvent);
        }

        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var converged = await db.PendingInvoices.AsNoTracking()
                .SingleAsync(r => r.OrderId == orderId, ct);

            using (new AssertionScope())
            {
                converged.OrderId.Should().Be(orderId);
                converged.PaymentId.Should().Be(paymentTransactionId);
                converged.BuyerId.Should().Be(buyerId);
                converged.OrderPayload.Should().NotBeNullOrEmpty();
                AssertOrderPayloadMatches(converged.OrderPayload!, orderEvent);
                converged.PaymentPayload.Should().NotBeNullOrEmpty();
                converged.FirstSeenAtUtc.Should().Be(PaymentArrivalUtc, "payment was first");
                converged.CompletedAtUtc.Should().Be(orderClock.GetUtcNow());
                converged.IssuedInvoiceId.Should().BeNull();
            }
        }
    }

    [Fact]
    public async Task Example_1_3_DuplicateOrderConfirmedEvent_RowStaysIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var orderId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();

        var firstClock = new FakeTimeProvider(OrderArrivalUtc);
        var secondClock = new FakeTimeProvider(OrderArrivalUtc.AddMinutes(2));

        var firstEvent = BuildOrderConfirmedEvent(orderId, buyerId);
        // Second arrival deliberately differs so the assertion proves the row
        // keeps the FIRST payload — locks in ADR-0020 / Wave 1.5 contract:
        // first-arrival wins, second arrival never overwrites OrderPayload.
        var secondEvent = BuildOrderConfirmedEvent(
            orderId, buyerId, totalOverride: 999.99m);

        // First arrival inserts.
        await using (var firstScope = _fixture.CreateScope())
        {
            var db = firstScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var handler = new OrderConfirmedInvoiceProjectionKafkaHandler(
                db,
                M7CommandHandlerStubs.NoOpIssueInvoiceHandler(),
                firstClock,
                NullLogger<OrderConfirmedInvoiceProjectionKafkaHandler>.Instance);

            await handler.Handle(BuildContext(ct), firstEvent);
        }

        // Same OrderId redelivered — handler must no-op.
        await using (var secondScope = _fixture.CreateScope())
        {
            var db = secondScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var handler = new OrderConfirmedInvoiceProjectionKafkaHandler(
                db,
                M7CommandHandlerStubs.NoOpIssueInvoiceHandler(),
                secondClock,
                NullLogger<OrderConfirmedInvoiceProjectionKafkaHandler>.Instance);

            await handler.Handle(BuildContext(ct), secondEvent);
        }

        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();

            var rows = await db.PendingInvoices.AsNoTracking()
                .Where(r => r.OrderId == orderId)
                .ToListAsync(ct);

            using (new AssertionScope())
            {
                rows.Should().HaveCount(1, "duplicate same-OrderId arrival is absorbed");
                rows[0].FirstSeenAtUtc.Should().Be(OrderArrivalUtc, "FirstSeenAtUtc is never overwritten");
                rows[0].PaymentId.Should().BeNull("payment half never arrived");
                rows[0].CompletedAtUtc.Should().BeNull();
                AssertOrderPayloadMatches(rows[0].OrderPayload!, firstEvent);
            }
        }
    }

    private static IMessageContext BuildContext(CancellationToken ct)
    {
        // Projection handlers source the convergence key (OrderId) from the Avro payload, not a
        // header; the context only needs the cancellation token.
        var context = Substitute.For<IMessageContext>();
        context.Headers.Returns(new MessageHeaders());
        var consumerContext = Substitute.For<IConsumerContext>();
        consumerContext.WorkerStopped.Returns(ct);
        context.ConsumerContext.Returns(consumerContext);
        return context;
    }

    private static AvroOrderConfirmedEvent BuildOrderConfirmedEvent(
        Guid orderId,
        Guid buyerId,
        DateTime? confirmedAtUtc = null,
        decimal? totalOverride = null)
    {
        // Use the production scale-4 helper so the test exercises the same
        // wire path as Ordering's OrderConfirmedMapper. Avoid the bare
        // AvroDecimal(decimal) ctor — it preserves the .NET decimal's native
        // scale and would never trip a scale-mismatch bug in the projection.
        const int Scale = 4;
        var lineTotal = totalOverride ?? 152.00m;
        return new AvroOrderConfirmedEvent
        {
            OrderId = orderId,
            BuyerId = buyerId,
            ConfirmedAtUtc = confirmedAtUtc ?? OrderArrivalUtc.UtcDateTime,
            Items =
            [
                new AvroOrderItemConfirmed
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
            BillingAddress = new AvroOrderBillingAddress
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

    private static AvroPaymentCapturedEvent BuildPaymentCapturedEvent(
        Guid orderId, Guid paymentTransactionId, Guid buyerId)
    {
        return new AvroPaymentCapturedEvent
        {
            OrderId = orderId,
            UserId = buyerId,
            PaymentTransactionId = paymentTransactionId,
            AuthorizationId = "auth-test",
            Amount = new Avro.AvroDecimal(152.00m),
            Currency = "EUR",
            CapturedAtUtc = PaymentArrivalUtc.UtcDateTime,
        };
    }

    private static void AssertOrderPayloadMatches(string orderPayloadJson, AvroOrderConfirmedEvent expected)
    {
        var dto = JsonSerializer.Deserialize<OrderPayloadDto>(orderPayloadJson)
                  ?? throw new InvalidOperationException("OrderPayload failed to deserialise.");

        dto.OrderId.Should().Be(expected.OrderId);
        dto.BuyerId.Should().Be(expected.BuyerId);
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
    /// <see cref="OrderConfirmedInvoiceProjectionKafkaHandler.SerializePayload"/>.
    /// Lives here (not in production code) because will introduce its own
    /// strongly-typed reader; this test-only DTO documents the wire contract
    /// the projection currently produces.
    /// </summary>
    private sealed record OrderPayloadDto(
        Guid OrderId,
        Guid BuyerId,
        DateTime ConfirmedAtUtc,
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
