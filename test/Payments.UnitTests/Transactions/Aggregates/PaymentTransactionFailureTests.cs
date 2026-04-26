using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Payments.Domain.Transactions.Events;
using Payments.Domain.Transactions.ValueObjects;
using Platform.SharedKernel.Exceptions;

namespace Payments.UnitTests.Transactions.Aggregates;

public class PaymentTransactionFailureTests
{
    private readonly FakeTimeProvider _fakeTimeProvider = new();

    private DateTimeOffset UtcNow => _fakeTimeProvider.GetUtcNow();

    private FailureInfo BuildFailureInfo() =>
        new(FailureReason.InsufficientFunds, "insufficient_funds", UtcNow);

    [Fact]
    public void MarkAuthorizationFailed_FromRequested_TransitionsToFailedAndRaisesBothEventsInOrder()
    {
        var tx = PaymentTransactionFactory.Requested(UtcNow);
        tx.PopDomainEvents();
        var failureInfo = BuildFailureInfo();

        var result = tx.MarkAuthorizationFailed(failureInfo, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            tx.Status.Should().Be(PaymentStatus.Failed);
            tx.FailureInfo.Should().Be(failureInfo);

            var events = tx.PopDomainEvents();
            events.Should().HaveCount(2);
            events[0].Should().BeOfType<PaymentAuthorizationFailedDomainEvent>();
            events[1].Should().BeOfType<PaymentFailedDomainEvent>();

            ((PaymentAuthorizationFailedDomainEvent)events[0]).FailureInfo.Should().Be(failureInfo);
            var failed = (PaymentFailedDomainEvent)events[1];
            failed.FailureInfo.Should().Be(failureInfo);
            failed.FailedAtUtc.Should().Be(UtcNow);
        }
    }

    [Fact]
    public void MarkAuthorizationFailed_WhenAlreadyFailed_ReturnsOkAndDoesNotRaiseEvents()
    {
        var t0 = UtcNow;
        var tx = PaymentTransactionFactory.Failed(t0);
        var failureInfoBefore = tx.FailureInfo;
        _fakeTimeProvider.Advance(TimeSpan.FromMinutes(5));

        var result = tx.MarkAuthorizationFailed(BuildFailureInfo(), UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            tx.FailureInfo.Should().Be(failureInfoBefore, "idempotent replay must not rewrite the original failure metadata");
            tx.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(nameof(PaymentStatus.Voided))]
    [InlineData(nameof(PaymentStatus.Refunded))]
    public void MarkAuthorizationFailed_WhenTerminalOtherThanFailed_ThrowsDataIntegrityException(string statusName)
    {
        var tx = statusName switch
        {
            nameof(PaymentStatus.Voided) => PaymentTransactionFactory.Voided(UtcNow),
            nameof(PaymentStatus.Refunded) => PaymentTransactionFactory.Refunded(UtcNow),
            _ => throw new InvalidOperationException(statusName),
        };

        var action = () => tx.MarkAuthorizationFailed(BuildFailureInfo(), UtcNow);

        action.Should().Throw<DataIntegrityException>();
    }

    [Fact]
    public void MarkCaptureFailed_FromAuthorized_TransitionsToFailedAndRaisesBothEventsInOrder()
    {
        var tx = PaymentTransactionFactory.Authorized(UtcNow);
        var existingGatewayTransactionId = tx.GatewayTransactionId;
        var failureInfo = BuildFailureInfo();

        var result = tx.MarkCaptureFailed(failureInfo, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            tx.Status.Should().Be(PaymentStatus.Failed);
            tx.FailureInfo.Should().Be(failureInfo);

            var events = tx.PopDomainEvents();
            events.Should().HaveCount(2);
            events[0].Should().BeOfType<PaymentCaptureFailedDomainEvent>();
            events[1].Should().BeOfType<PaymentFailedDomainEvent>();

            var captureFailed = (PaymentCaptureFailedDomainEvent)events[0];
            captureFailed.GatewayTransactionId.Should().Be(existingGatewayTransactionId);
            captureFailed.FailureInfo.Should().Be(failureInfo);
        }
    }

    [Fact]
    public void MarkCaptureFailed_FromRequested_ThrowsDataIntegrityException()
    {
        var tx = PaymentTransactionFactory.Requested(UtcNow);
        tx.PopDomainEvents();

        var action = () => tx.MarkCaptureFailed(BuildFailureInfo(), UtcNow);

        action.Should().Throw<DataIntegrityException>()
            .WithMessage("*MarkCaptureFailed is only valid from 'Authorized'*");
    }

    [Fact]
    public void MarkAuthorizationFailed_FromAuthorized_ThrowsDataIntegrityException()
    {
        var tx = PaymentTransactionFactory.Authorized(UtcNow);

        var action = () => tx.MarkAuthorizationFailed(BuildFailureInfo(), UtcNow);

        action.Should().Throw<DataIntegrityException>()
            .WithMessage("*MarkAuthorizationFailed is only valid from 'Requested'*");
    }

    [Fact]
    public void MarkCaptureFailed_WhenAlreadyFailed_ReturnsOkAndDoesNotRaiseEvents()
    {
        var t0 = UtcNow;
        var tx = PaymentTransactionFactory.Failed(t0);
        var failureInfoBefore = tx.FailureInfo;
        _fakeTimeProvider.Advance(TimeSpan.FromMinutes(5));

        var result = tx.MarkCaptureFailed(BuildFailureInfo(), UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            tx.FailureInfo.Should().Be(failureInfoBefore, "idempotent replay must not rewrite the original failure metadata");
            tx.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void MarkCaptureFailed_FromCompleted_ThrowsDataIntegrityException()
    {
        var tx = PaymentTransactionFactory.Completed(UtcNow);

        var action = () => tx.MarkCaptureFailed(BuildFailureInfo(), UtcNow);

        action.Should().Throw<DataIntegrityException>();
    }
}
