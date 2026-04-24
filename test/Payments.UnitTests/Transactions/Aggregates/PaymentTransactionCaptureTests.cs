using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Payments.Domain.Transactions.Events;
using Payments.Domain.Transactions.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Exceptions;

namespace Payments.UnitTests.Transactions.Aggregates;

public class PaymentTransactionCaptureTests
{
    private readonly FakeTimeProvider _fakeTimeProvider = new();

    private DateTimeOffset UtcNow => _fakeTimeProvider.GetUtcNow();

    [Fact]
    public void Capture_FromAuthorized_TransitionsToCompletedAndRaisesBothEventsInOrder()
    {
        var tx = PaymentTransactionFactory.Authorized(UtcNow);

        var result = tx.Capture(
            PaymentTransactionFactory.DefaultGatewayTransactionId,
            PaymentTransactionFactory.SuccessResponse,
            UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            tx.Status.Should().Be(PaymentStatus.Completed);
            tx.CapturedAtUtc.Should().Be(UtcNow);
            tx.CompletedAtUtc.Should().Be(UtcNow);

            var events = tx.PopDomainEvents();
            events.Should().HaveCount(2);
            events[0].Should().BeOfType<PaymentCapturedDomainEvent>();
            events[1].Should().BeOfType<PaymentCompletedDomainEvent>();

            var captured = (PaymentCapturedDomainEvent)events[0];
            captured.GatewayTransactionId.Should().Be(PaymentTransactionFactory.DefaultGatewayTransactionId);
            captured.CapturedAtUtc.Should().Be(UtcNow);

            var completed = (PaymentCompletedDomainEvent)events[1];
            completed.CompletedAtUtc.Should().Be(UtcNow);
        }
    }

    [Fact]
    public void Capture_WhenAlreadyCompleted_ReturnsOkAndDoesNotRaiseAdditionalEvents()
    {
        var t0 = UtcNow;
        var tx = PaymentTransactionFactory.Completed(t0);
        _fakeTimeProvider.Advance(TimeSpan.FromMinutes(5));

        var result = tx.Capture(
            PaymentTransactionFactory.DefaultGatewayTransactionId,
            PaymentTransactionFactory.SuccessResponse,
            UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            tx.Status.Should().Be(PaymentStatus.Completed);
            tx.CapturedAtUtc.Should().Be(t0, "idempotent replay must not rewrite the original timestamp");
            tx.CompletedAtUtc.Should().Be(t0, "idempotent replay must not rewrite the original timestamp");
            tx.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void Capture_FromRequested_ThrowsDataIntegrityException()
    {
        var tx = PaymentTransactionFactory.Requested(UtcNow);

        var action = () => tx.Capture("gw-tx-abc", PaymentTransactionFactory.SuccessResponse, UtcNow);

        action.Should().Throw<DataIntegrityException>()
            .WithMessage("*Invalid payment status transition*");
    }

    [Theory]
    [InlineData(nameof(PaymentStatus.Failed))]
    [InlineData(nameof(PaymentStatus.Voided))]
    [InlineData(nameof(PaymentStatus.Refunded))]
    public void Capture_WhenTerminal_ThrowsDataIntegrityException(string statusName)
    {
        var tx = statusName switch
        {
            nameof(PaymentStatus.Failed) => PaymentTransactionFactory.Failed(UtcNow),
            nameof(PaymentStatus.Voided) => PaymentTransactionFactory.Voided(UtcNow),
            nameof(PaymentStatus.Refunded) => PaymentTransactionFactory.Refunded(UtcNow),
            _ => throw new InvalidOperationException(statusName),
        };

        var action = () => tx.Capture(
            PaymentTransactionFactory.DefaultGatewayTransactionId,
            PaymentTransactionFactory.SuccessResponse,
            UtcNow);

        action.Should().Throw<DataIntegrityException>();
    }

    [Fact]
    public void Capture_WhenDifferentGatewayTransactionId_ThrowsDataIntegrityException()
    {
        var tx = PaymentTransactionFactory.Authorized(UtcNow);

        var action = () => tx.Capture("gw-tx-DIFFERENT", PaymentTransactionFactory.SuccessResponse, UtcNow);

        action.Should().Throw<DataIntegrityException>()
            .WithMessage("*GatewayTransactionId is append-only*");
    }
}
