using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Payments.Domain.Transactions.Events;
using Payments.Domain.Transactions.ValueObjects;
using Platform.SharedKernel.Exceptions;

namespace Payments.UnitTests.Transactions.Aggregates;

public class PaymentTransactionRefundTests
{
    private readonly FakeTimeProvider _fakeTimeProvider = new();

    private DateTimeOffset UtcNow => _fakeTimeProvider.GetUtcNow();

    [Fact]
    public void Refund_FromCompleted_TransitionsToRefundedAndRaisesEvent()
    {
        var tx = PaymentTransactionFactory.Completed(UtcNow);

        var result = tx.Refund("customer_cancelled", PaymentTransactionFactory.SuccessResponse, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            tx.Status.Should().Be(PaymentStatus.Refunded);
            tx.RefundedAtUtc.Should().Be(UtcNow);

            var evt = tx.PopDomainEvents().Should().ContainSingle()
                .Which.Should().BeOfType<PaymentRefundedDomainEvent>().Subject;
            evt.Reason.Should().Be("customer_cancelled");
            evt.Amount.Should().Be(tx.Amount);
            evt.RefundedAtUtc.Should().Be(UtcNow);
            evt.GatewayTransactionId.Should().Be(PaymentTransactionFactory.DefaultGatewayTransactionId);
        }
    }

    [Fact]
    public void Refund_WhenAlreadyRefunded_ReturnsOkAndDoesNotRaiseEvent()
    {
        var t0 = UtcNow;
        var tx = PaymentTransactionFactory.Refunded(t0);
        _fakeTimeProvider.Advance(TimeSpan.FromMinutes(5));

        var result = tx.Refund("customer_cancelled", PaymentTransactionFactory.SuccessResponse, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            tx.RefundedAtUtc.Should().Be(t0, "idempotent replay must not rewrite the original timestamp");
            tx.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void Refund_FromAuthorized_ThrowsDataIntegrityException()
    {
        var tx = PaymentTransactionFactory.Authorized(UtcNow);

        var action = () => tx.Refund("customer_cancelled", PaymentTransactionFactory.SuccessResponse, UtcNow);

        action.Should().Throw<DataIntegrityException>();
    }

    [Fact]
    public void Refund_FromRequested_ThrowsDataIntegrityException()
    {
        var tx = PaymentTransactionFactory.Requested();

        var action = () => tx.Refund("customer_cancelled", PaymentTransactionFactory.SuccessResponse, UtcNow);

        action.Should().Throw<DataIntegrityException>();
    }

    [Theory]
    [InlineData(nameof(PaymentStatus.Failed))]
    [InlineData(nameof(PaymentStatus.Voided))]
    public void Refund_WhenTerminalOtherThanRefunded_ThrowsDataIntegrityException(string statusName)
    {
        var tx = statusName switch
        {
            nameof(PaymentStatus.Failed) => PaymentTransactionFactory.Failed(UtcNow),
            nameof(PaymentStatus.Voided) => PaymentTransactionFactory.Voided(UtcNow),
            _ => throw new InvalidOperationException(statusName),
        };

        var action = () => tx.Refund("customer_cancelled", PaymentTransactionFactory.SuccessResponse, UtcNow);

        action.Should().Throw<DataIntegrityException>();
    }

    [Fact]
    public void Refund_WithEmptyReason_ThrowsArgumentException()
    {
        var tx = PaymentTransactionFactory.Completed(UtcNow);

        var action = () => tx.Refund("", PaymentTransactionFactory.SuccessResponse, UtcNow);

        action.Should().Throw<ArgumentException>();
    }
}
