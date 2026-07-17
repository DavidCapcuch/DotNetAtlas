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
        // Arrange
        var tx = PaymentTransactionFactory.Completed(UtcNow);

        // Act
        var result = tx.Refund("customer_cancelled", PaymentTransactionFactory.SuccessResponse, UtcNow);

        // Assert
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
        // Arrange
        var t0 = UtcNow;
        var tx = PaymentTransactionFactory.Refunded(t0);
        _fakeTimeProvider.Advance(TimeSpan.FromMinutes(5));

        // Act
        var result = tx.Refund("customer_cancelled", PaymentTransactionFactory.SuccessResponse, UtcNow);

        // Assert
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
        // Arrange
        var tx = PaymentTransactionFactory.Authorized(UtcNow);

        // Act
        var action = () => tx.Refund("customer_cancelled", PaymentTransactionFactory.SuccessResponse, UtcNow);

        // Assert
        action.Should().Throw<DataIntegrityException>();
    }

    [Fact]
    public void Refund_FromRequested_ThrowsDataIntegrityException()
    {
        // Arrange
        var tx = PaymentTransactionFactory.Requested();

        // Act
        var action = () => tx.Refund("customer_cancelled", PaymentTransactionFactory.SuccessResponse, UtcNow);

        // Assert
        action.Should().Throw<DataIntegrityException>();
    }

    [Theory]
    [InlineData(nameof(PaymentStatus.Failed))]
    [InlineData(nameof(PaymentStatus.Voided))]
    public void Refund_WhenTerminalOtherThanRefunded_ThrowsDataIntegrityException(string statusName)
    {
        // Arrange
        var tx = statusName switch
        {
            nameof(PaymentStatus.Failed) => PaymentTransactionFactory.Failed(UtcNow),
            nameof(PaymentStatus.Voided) => PaymentTransactionFactory.Voided(UtcNow),
            _ => throw new InvalidOperationException(statusName),
        };

        // Act
        var action = () => tx.Refund("customer_cancelled", PaymentTransactionFactory.SuccessResponse, UtcNow);

        // Assert
        action.Should().Throw<DataIntegrityException>();
    }

    [Fact]
    public void Refund_WithEmptyReason_ThrowsArgumentException()
    {
        // Arrange
        var tx = PaymentTransactionFactory.Completed(UtcNow);

        // Act
        var action = () => tx.Refund("", PaymentTransactionFactory.SuccessResponse, UtcNow);

        // Assert
        action.Should().Throw<ArgumentException>();
    }
}
