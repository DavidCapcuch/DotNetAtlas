using Avro;
using Payments.Application.Outbox;
using Payments.Domain.Transactions.Events;
using Payments.Domain.Transactions.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Payments.UnitTests.Application.Outbox;

/// <summary>
/// Field-level mapping tests for the 6 outbox mappers. Each test verifies the locked Avro
/// shape (per <c>events-catalog.md § 2</c>) is produced from the internal domain event under
/// Path B in the M4 plan: <c>BuyerId → UserId</c>; <c>GatewayTransactionId → AuthorizationId</c>;
/// <c>OrderId</c> dropped; sentinels documented inline.
/// </summary>
public class PaymentEventMapperTests
{
    private const string GatewayTransactionId = "gw-tx-abc";

    private static readonly DateTimeOffset Now = new(2026, 4, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PaymentId = Guid.CreateVersion7();
    private static readonly Guid CorrelationId = Guid.CreateVersion7();
    private static readonly Guid BuyerId = Guid.CreateVersion7();
    private static readonly Guid OrderId = Guid.CreateVersion7();

    private static Money UsdAmount(decimal amount = 100m) => Money.Create(amount, "USD").Value;

    [Fact]
    public void PaymentAuthorizedMapper_ProjectsExpiresAtUtcFromDomainEvent_NotSynthesized()
    {
        // H-6: the mapper must source ExpiresAtUtc from the gateway response (carried through the
        // domain event) — not synthesize AuthorizedAtUtc + 7 days inline. Test passes a deliberately
        // out-of-band ExpiresAtUtc so a regression that re-introduces inline synthesis fails loudly.
        var gatewayExpiry = Now.AddDays(14);
        var domainEvent = new PaymentAuthorizedDomainEvent
        {
            PaymentId = PaymentId,
            CorrelationId = CorrelationId,
            BuyerId = BuyerId,
            OrderId = OrderId,
            GatewayTransactionId = GatewayTransactionId,
            Amount = UsdAmount(99.99m),
            AuthorizedAtUtc = Now,
            ExpiresAtUtc = gatewayExpiry,
            OccurredOnUtc = Now,
        };

        var avro = domainEvent.ToPaymentAuthorizedEvent();

        using (new AssertionScope())
        {
            avro.CorrelationId.Should().Be(CorrelationId);
            avro.UserId.Should().Be(BuyerId);
            avro.AuthorizationId.Should().Be(GatewayTransactionId);
            avro.Amount.Should().Be(new AvroDecimal(99.9900m));
            avro.Currency.Should().Be("USD");
            avro.AuthorizedAtUtc.Should().Be(Now.UtcDateTime);
            avro.ExpiresAtUtc.Should().Be(gatewayExpiry.UtcDateTime);
        }
    }

    [Fact]
    public void PaymentAuthorizationFailedMapper_MapsErrorCodeFromGatewayCodeWhenAvailable()
    {
        var failureInfo = new FailureInfo(FailureReason.InsufficientFunds, "insufficient_funds", Now);
        var domainEvent = new PaymentAuthorizationFailedDomainEvent
        {
            PaymentId = PaymentId,
            CorrelationId = CorrelationId,
            BuyerId = BuyerId,
            OrderId = OrderId,
            FailureInfo = failureInfo,
            OccurredOnUtc = Now,
        };

        var avro = domainEvent.ToPaymentAuthorizationFailedEvent();

        using (new AssertionScope())
        {
            avro.CorrelationId.Should().Be(CorrelationId);
            avro.UserId.Should().Be(BuyerId);
            avro.ErrorCode.Should().Be("insufficient_funds");
            avro.ErrorMessage.Should().Be("InsufficientFunds");
            avro.FailedAtUtc.Should().Be(Now.UtcDateTime);
        }
    }

    [Fact]
    public void PaymentAuthorizationFailedMapper_FallsBackToReasonNameWhenGatewayCodeMissing()
    {
        var failureInfo = new FailureInfo(FailureReason.Unknown, GatewayCode: null, Now);
        var domainEvent = new PaymentAuthorizationFailedDomainEvent
        {
            PaymentId = PaymentId,
            CorrelationId = CorrelationId,
            BuyerId = BuyerId,
            OrderId = OrderId,
            FailureInfo = failureInfo,
            OccurredOnUtc = Now,
        };

        var avro = domainEvent.ToPaymentAuthorizationFailedEvent();

        avro.ErrorCode.Should().Be("Unknown");
    }

    public static TheoryData<int, bool> RetryableByReason() => new()
    {
        { FailureReason.GatewayTimeout.Value, true },
        { FailureReason.GatewayDeclined.Value, false },
        { FailureReason.InsufficientFunds.Value, false },
        { FailureReason.FraudSuspected.Value, false },
        { FailureReason.Cancelled.Value, false },
        { FailureReason.Unknown.Value, false },
    };

    [Theory]
    [MemberData(nameof(RetryableByReason))]
    public void PaymentAuthorizationFailedMapper_ProjectsIsRetryableFromFailureReason(
        int reasonValue, bool expectedIsRetryable)
    {
        var reason = FailureReason.FromValue(reasonValue);
        var failureInfo = new FailureInfo(reason, GatewayCode: null, Now);
        var domainEvent = new PaymentAuthorizationFailedDomainEvent
        {
            PaymentId = PaymentId,
            CorrelationId = CorrelationId,
            BuyerId = BuyerId,
            OrderId = OrderId,
            FailureInfo = failureInfo,
            OccurredOnUtc = Now,
        };

        var avro = domainEvent.ToPaymentAuthorizationFailedEvent();

        avro.IsRetryable.Should().Be(expectedIsRetryable);
    }

    [Theory]
    [MemberData(nameof(RetryableByReason))]
    public void PaymentCaptureFailedMapper_ProjectsIsRetryableFromFailureReason(
        int reasonValue, bool expectedIsRetryable)
    {
        var reason = FailureReason.FromValue(reasonValue);
        var failureInfo = new FailureInfo(reason, GatewayCode: null, Now);
        var domainEvent = new PaymentCaptureFailedDomainEvent
        {
            PaymentId = PaymentId,
            CorrelationId = CorrelationId,
            BuyerId = BuyerId,
            OrderId = OrderId,
            GatewayTransactionId = GatewayTransactionId,
            FailureInfo = failureInfo,
            OccurredOnUtc = Now,
        };

        var avro = domainEvent.ToPaymentCaptureFailedEvent();

        avro.IsRetryable.Should().Be(expectedIsRetryable);
    }

    [Fact]
    public void PaymentCapturedMapper_MapsAggregateIdToPaymentTransactionId()
    {
        var domainEvent = new PaymentCapturedDomainEvent
        {
            PaymentId = PaymentId,
            CorrelationId = CorrelationId,
            BuyerId = BuyerId,
            OrderId = OrderId,
            GatewayTransactionId = GatewayTransactionId,
            Amount = UsdAmount(),
            CapturedAtUtc = Now,
            OccurredOnUtc = Now,
        };

        var avro = domainEvent.ToPaymentCapturedEvent();

        using (new AssertionScope())
        {
            avro.CorrelationId.Should().Be(CorrelationId);
            avro.UserId.Should().Be(BuyerId);
            avro.PaymentTransactionId.Should().Be(PaymentId);
            avro.AuthorizationId.Should().Be(GatewayTransactionId);
            avro.Amount.Should().Be(new AvroDecimal(100.0000m));
            avro.Currency.Should().Be("USD");
            avro.CapturedAtUtc.Should().Be(Now.UtcDateTime);
        }
    }

    [Fact]
    public void PaymentCaptureFailedMapper_PopulatesAuthorizationIdFromDomainEvent()
    {
        var failureInfo = new FailureInfo(FailureReason.GatewayDeclined, "card_declined", Now);
        var domainEvent = new PaymentCaptureFailedDomainEvent
        {
            PaymentId = PaymentId,
            CorrelationId = CorrelationId,
            BuyerId = BuyerId,
            OrderId = OrderId,
            GatewayTransactionId = GatewayTransactionId,
            FailureInfo = failureInfo,
            OccurredOnUtc = Now,
        };

        var avro = domainEvent.ToPaymentCaptureFailedEvent();

        using (new AssertionScope())
        {
            avro.CorrelationId.Should().Be(CorrelationId);
            avro.UserId.Should().Be(BuyerId);
            avro.AuthorizationId.Should().Be(GatewayTransactionId);
            avro.ErrorCode.Should().Be("card_declined");
            avro.ErrorMessage.Should().Be("GatewayDeclined");
        }
    }

    [Fact]
    public void PaymentRefundedMapper_GeneratesFreshRefundTransactionIdDistinctFromPaymentTransactionId()
    {
        // #246: RefundTransactionId is a fresh UUID v7 — downstream consumers (Notifications
        // refund email, Invoicing credit-note pairing) key off it as a distinct identifier and
        // would alias-collide if it equalled PaymentTransactionId. Two calls also produce
        // different RefundTransactionId values (v7 monotonicity sanity).
        var domainEvent = new PaymentRefundedDomainEvent
        {
            PaymentId = PaymentId,
            CorrelationId = CorrelationId,
            BuyerId = BuyerId,
            OrderId = OrderId,
            GatewayTransactionId = GatewayTransactionId,
            Amount = UsdAmount(50m),
            Reason = "saga_compensation",
            RefundedAtUtc = Now,
            OccurredOnUtc = Now,
        };

        var avro = domainEvent.ToPaymentRefundedEvent();
        var second = domainEvent.ToPaymentRefundedEvent();

        using (new AssertionScope())
        {
            avro.CorrelationId.Should().Be(CorrelationId);
            avro.UserId.Should().Be(BuyerId);
            avro.PaymentTransactionId.Should().Be(PaymentId);
            avro.RefundTransactionId.Should().NotBe(PaymentId);
            avro.RefundTransactionId.Should().NotBe(avro.PaymentTransactionId);
            second.RefundTransactionId.Should().NotBe(avro.RefundTransactionId);
            avro.RefundedAmount.Should().Be(new AvroDecimal(50.0000m));
            avro.Currency.Should().Be("USD");
            avro.RefundedAtUtc.Should().Be(Now.UtcDateTime);
        }
    }

    [Fact]
    public void PaymentVoidedMapper_MapsAllFieldsIncludingReason()
    {
        var domainEvent = new PaymentVoidedDomainEvent
        {
            PaymentId = PaymentId,
            CorrelationId = CorrelationId,
            BuyerId = BuyerId,
            OrderId = OrderId,
            GatewayTransactionId = GatewayTransactionId,
            VoidedAtUtc = Now,
            Reason = "saga_compensation_pre_capture",
            OccurredOnUtc = Now,
        };

        var avro = domainEvent.ToPaymentVoidedEvent();

        using (new AssertionScope())
        {
            avro.CorrelationId.Should().Be(CorrelationId);
            avro.UserId.Should().Be(BuyerId);
            avro.AuthorizationId.Should().Be(GatewayTransactionId);
            avro.VoidedAtUtc.Should().Be(Now.UtcDateTime);
            avro.Reason.Should().Be("saga_compensation_pre_capture");
        }
    }
}
