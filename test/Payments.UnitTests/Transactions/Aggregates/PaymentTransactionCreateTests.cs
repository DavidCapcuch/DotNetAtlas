using FluentResults.Extensions.FluentAssertions;
using Payments.Domain.Transactions;
using Payments.Domain.Transactions.ValueObjects;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.ValueObjects;

namespace Payments.UnitTests.Transactions.Aggregates;

public class PaymentTransactionCreateTests
{
    [Fact]
    public void Create_WhenValid_ReturnsOkAndRaisesNoDomainEvents()
    {
        // ADR-0023 follow-up: PaymentTransaction.Create no longer raises a domain event. The
        // wire-level "requested" signal is RequestPaymentCommand (renamed from PaymentRequestedEvent
        // and moved to payments.payment-commands), produced by the Checkout saga — not by Payments.
        // The Payments-internal PaymentRequestedDomainEvent had no in-process handler and no
        // outbox publisher; it was scaffolding and has been removed.
        var paymentId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var amount = Money.Create(100m, "USD").Value;

        var result = PaymentTransaction.Create(
            paymentId, correlationId, buyerId, orderId, amount, "tok_visa_4242");

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            var tx = result.Value;
            tx.Id.Should().Be(paymentId);
            tx.CorrelationId.Should().Be(correlationId);
            tx.BuyerId.Should().Be(buyerId);
            tx.OrderId.Should().Be(orderId);
            tx.Amount.Should().Be(amount);
            tx.PaymentMethodId.Value.Should().Be("tok_visa_4242");
            tx.Status.Should().Be(PaymentStatus.Requested);
            tx.GatewayTransactionId.Should().BeNull();
            tx.AuthorizedAtUtc.Should().BeNull();
            tx.FailureInfo.Should().BeNull();

            tx.PopDomainEvents().Should().BeEmpty(
                "ADR-0023 follow-up: PaymentTransaction.Create raises no domain events");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Create_WhenAmountNotPositive_ReturnsInvalidAmount(decimal amount)
    {
        // School B: Money is a signed quantity; positivity is the Payments BC's invariant
        // and lives at PaymentTransaction.Create. Confirm the local guard catches
        // zero/negative amounts and surfaces Payments.InvalidAmount.
        var nonPositiveAmount = Money.Create(amount, "USD").Value;

        var result = PaymentTransaction.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            nonPositiveAmount, "tok_visa_4242");

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var error = result.Errors[0] as ValidationError;
            error.Should().NotBeNull();
            error!.ErrorCode.Should().Be("Payments.InvalidAmount");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Create_WhenPaymentMethodIdEmpty_ReturnsInvalidPaymentMethod(string? paymentMethodId)
    {
        var amount = Money.Create(100m, "USD").Value;

        var result = PaymentTransaction.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            amount, paymentMethodId!);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var error = result.Errors[0] as ValidationError;
            error.Should().NotBeNull();
            error!.ErrorCode.Should().Be("Payments.InvalidPaymentMethod");
        }
    }

    [Fact]
    public void Create_WhenPaymentMethodIdTooLong_ReturnsInvalidPaymentMethod()
    {
        var amount = Money.Create(100m, "USD").Value;
        var tooLong = new string('x', 65);

        var result = PaymentTransaction.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            amount, tooLong);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var error = result.Errors[0] as ValidationError;
            error!.ErrorCode.Should().Be("Payments.InvalidPaymentMethod");
        }
    }
}
