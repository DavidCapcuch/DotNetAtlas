using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Payments.Domain.Transactions.Events;
using Payments.Domain.Transactions.ValueObjects;
using Platform.SharedKernel.Exceptions;

namespace Payments.UnitTests.Transactions.Aggregates;

public class PaymentTransactionVoidTests
{
    private readonly FakeTimeProvider _fakeTimeProvider = new();

    private DateTimeOffset UtcNow => _fakeTimeProvider.GetUtcNow();

    [Fact]
    public void Void_FromAuthorized_TransitionsToVoidedAndRaisesSingleEvent()
    {
        var tx = PaymentTransactionFactory.Authorized(UtcNow);

        var result = tx.Void(PaymentTransactionFactory.SuccessResponse, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            tx.Status.Should().Be(PaymentStatus.Voided);
            tx.VoidedAtUtc.Should().Be(UtcNow);

            var evt = tx.PopDomainEvents().Should().ContainSingle()
                .Which.Should().BeOfType<PaymentVoidedDomainEvent>().Subject;
            evt.GatewayTransactionId.Should().Be(PaymentTransactionFactory.DefaultGatewayTransactionId);
            evt.VoidedAtUtc.Should().Be(UtcNow);
        }
    }

    [Fact]
    public void Void_WhenAlreadyVoided_ReturnsOkAndDoesNotRaiseEvent()
    {
        var t0 = UtcNow;
        var tx = PaymentTransactionFactory.Voided(t0);
        _fakeTimeProvider.Advance(TimeSpan.FromMinutes(5));

        var result = tx.Void(PaymentTransactionFactory.SuccessResponse, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            tx.VoidedAtUtc.Should().Be(t0, "idempotent replay must not rewrite the original timestamp");
            tx.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void Void_FromRequested_ThrowsDataIntegrityException()
    {
        var tx = PaymentTransactionFactory.Requested(UtcNow);

        var action = () => tx.Void(PaymentTransactionFactory.SuccessResponse, UtcNow);

        action.Should().Throw<DataIntegrityException>();
    }

    [Theory]
    [InlineData(nameof(PaymentStatus.Failed))]
    [InlineData(nameof(PaymentStatus.Refunded))]
    public void Void_WhenTerminalOtherThanVoided_ThrowsDataIntegrityException(string statusName)
    {
        var tx = statusName switch
        {
            nameof(PaymentStatus.Failed) => PaymentTransactionFactory.Failed(UtcNow),
            nameof(PaymentStatus.Refunded) => PaymentTransactionFactory.Refunded(UtcNow),
            _ => throw new InvalidOperationException(statusName),
        };

        var action = () => tx.Void(PaymentTransactionFactory.SuccessResponse, UtcNow);

        action.Should().Throw<DataIntegrityException>();
    }

    [Fact]
    public void Void_FromCompleted_ThrowsDataIntegrityException()
    {
        var tx = PaymentTransactionFactory.Completed(UtcNow);

        var action = () => tx.Void(PaymentTransactionFactory.SuccessResponse, UtcNow);

        action.Should().Throw<DataIntegrityException>();
    }
}
