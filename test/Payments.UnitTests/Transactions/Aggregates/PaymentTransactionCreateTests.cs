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
        // Arrange
        // Per ADR-0023, PaymentTransaction.Create raises no domain events. The wire-level
        // "requested" signal is RequestPaymentCommand (on payments.payment-commands), produced by the
        // Checkout saga — not by Payments. A Payments-internal "requested" domain event would have no
        // in-process handler and no outbox publisher, so none is raised.
        var paymentId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var amount = Money.Create(100m, "USD").Value;

        // Act
        var result = PaymentTransaction.Create(
            paymentId, buyerId, orderId, amount, "tok_visa_4242");

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            var tx = result.Value;
            tx.Id.Should().Be(paymentId);
            tx.BuyerId.Should().Be(buyerId);
            tx.OrderId.Should().Be(orderId);
            tx.Amount.Should().Be(amount);
            tx.PaymentMethodId.Value.Should().Be("tok_visa_4242");
            tx.Status.Should().Be(PaymentStatus.Requested);
            tx.GatewayTransactionId.Should().BeNull();
            tx.AuthorizedAtUtc.Should().BeNull();
            tx.FailureInfo.Should().BeNull();

            tx.PopDomainEvents().Should().BeEmpty(
                "per ADR-0023, PaymentTransaction.Create raises no domain events");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Create_WhenAmountNotPositive_ReturnsInvalidAmount(decimal amount)
    {
        // Arrange
        // Money is a signed quantity; positivity is the Payments BC's invariant
        // and lives at PaymentTransaction.Create. Confirm the local guard catches
        // zero/negative amounts and surfaces Payments.InvalidAmount.
        var nonPositiveAmount = Money.Create(amount, "USD").Value;

        // Act
        var result = PaymentTransaction.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            nonPositiveAmount, "tok_visa_4242");

        // Assert
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
        // Arrange
        var amount = Money.Create(100m, "USD").Value;

        // Act
        var result = PaymentTransaction.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            amount, paymentMethodId!);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var error = result.Errors[0] as ValidationError;
            error.Should().NotBeNull();
            error!.ErrorCode.Should().Be("Payments.InvalidPaymentMethod");
        }
    }

    [Fact]
    [Trait("Category", "boundary")]
    public void Create_WhenPaymentMethodIdTooLong_ReturnsInvalidPaymentMethod()
    {
        // Arrange
        var amount = Money.Create(100m, "USD").Value;
        var tooLong = new string('x', 65);

        // Act
        var result = PaymentTransaction.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            amount, tooLong);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var error = result.Errors[0] as ValidationError;
            error!.ErrorCode.Should().Be("Payments.InvalidPaymentMethod");
        }
    }
}
