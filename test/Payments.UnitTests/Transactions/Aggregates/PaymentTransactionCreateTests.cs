using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Payments.Domain.Transactions;
using Payments.Domain.Transactions.Events;
using Payments.Domain.Transactions.ValueObjects;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.ValueObjects;

namespace Payments.UnitTests.Transactions.Aggregates;

public class PaymentTransactionCreateTests
{
    private readonly FakeTimeProvider _fakeTimeProvider = new();

    private DateTimeOffset UtcNow => _fakeTimeProvider.GetUtcNow();

    [Fact]
    public void Create_WhenValid_ReturnsOkAndRaisesPaymentRequestedDomainEvent()
    {
        var paymentId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var amount = Money.Create(100m, "USD").Value;

        var result = PaymentTransaction.Create(
            paymentId, correlationId, buyerId, orderId, amount, "tok_visa_4242", UtcNow);

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

            var domainEvent = tx.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<PaymentRequestedDomainEvent>()
                .Subject;
            domainEvent.PaymentId.Should().Be(paymentId);
            domainEvent.CorrelationId.Should().Be(correlationId);
            domainEvent.BuyerId.Should().Be(buyerId);
            domainEvent.OrderId.Should().Be(orderId);
            domainEvent.Amount.Should().Be(amount);
            domainEvent.PaymentMethodId.Value.Should().Be("tok_visa_4242");
            domainEvent.OccurredOnUtc.Should().Be(UtcNow);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Create_WhenAmountNotPositive_UpstreamMoneyFactoryRejects(decimal amount)
    {
        // Amount.Create itself enforces > 0. Test confirms the factory path never sees a zero/negative Money,
        // because Money.Create returns a failure before PaymentTransaction.Create is reached.
        var moneyResult = Money.Create(amount, "USD");

        using (new AssertionScope())
        {
            moneyResult.Should().BeFailure();
            var error = moneyResult.Errors[0] as ValidationError;
            error.Should().NotBeNull();
            error!.ErrorCode.Should().Be("Money.AmountMustBePositive");
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
            amount, paymentMethodId!, UtcNow);

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
            amount, tooLong, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var error = result.Errors[0] as ValidationError;
            error!.ErrorCode.Should().Be("Payments.InvalidPaymentMethod");
        }
    }
}
