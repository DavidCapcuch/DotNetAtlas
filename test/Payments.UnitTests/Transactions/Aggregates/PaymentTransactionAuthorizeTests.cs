using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Payments.Domain.Transactions.Events;
using Payments.Domain.Transactions.ValueObjects;
using Platform.SharedKernel.Exceptions;

namespace Payments.UnitTests.Transactions.Aggregates;

public class PaymentTransactionAuthorizeTests
{
    private readonly FakeTimeProvider _fakeTimeProvider = new();

    private DateTimeOffset UtcNow => _fakeTimeProvider.GetUtcNow();

    private DateTimeOffset ExpiresAt => UtcNow.AddDays(7);

    [Fact]
    public void Authorize_FromRequested_TransitionsToAuthorizedAndRaisesEvent()
    {
        // Arrange
        var tx = PaymentTransactionFactory.Requested();
        tx.PopDomainEvents();

        // Act
        var result = tx.Authorize("gw-tx-abc", PaymentTransactionFactory.SuccessResponse, ExpiresAt, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            tx.Status.Should().Be(PaymentStatus.Authorized);
            tx.GatewayTransactionId.Should().Be("gw-tx-abc");
            tx.AuthorizedAtUtc.Should().Be(UtcNow);

            var evt = tx.PopDomainEvents().Should().ContainSingle()
                .Which.Should().BeOfType<PaymentAuthorizedDomainEvent>().Subject;
            evt.GatewayTransactionId.Should().Be("gw-tx-abc");
            evt.AuthorizedAtUtc.Should().Be(UtcNow);
            evt.ExpiresAtUtc.Should().Be(ExpiresAt);
            evt.Amount.Should().Be(tx.Amount);
        }
    }

    [Fact]
    public void Authorize_WhenAlreadyAuthorized_ReturnsOkAndDoesNotRaiseEvent()
    {
        // Arrange
        var t0 = UtcNow;
        var tx = PaymentTransactionFactory.Authorized(t0);
        _fakeTimeProvider.Advance(TimeSpan.FromMinutes(5));

        // Act
        var result = tx.Authorize(
            PaymentTransactionFactory.DefaultGatewayTransactionId,
            PaymentTransactionFactory.SuccessResponse,
            ExpiresAt,
            UtcNow);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            tx.Status.Should().Be(PaymentStatus.Authorized);
            tx.AuthorizedAtUtc.Should().Be(t0, "idempotent replay must not rewrite the original timestamp");
            tx.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void Authorize_WhenDifferentGatewayTransactionId_ThrowsDataIntegrityException()
    {
        // Arrange
        var tx = PaymentTransactionFactory.Authorized(UtcNow);

        // Act
        var action = () => tx.Authorize("gw-tx-OTHER", PaymentTransactionFactory.SuccessResponse, ExpiresAt, UtcNow);

        // Assert
        action.Should().Throw<DataIntegrityException>()
            .WithMessage("*GatewayTransactionId is append-only*");
    }

    [Theory]
    [InlineData(nameof(PaymentStatus.Failed))]
    [InlineData(nameof(PaymentStatus.Voided))]
    [InlineData(nameof(PaymentStatus.Refunded))]
    public void Authorize_WhenTerminal_ThrowsDataIntegrityException(string statusName)
    {
        // Arrange
        var tx = statusName switch
        {
            nameof(PaymentStatus.Failed) => PaymentTransactionFactory.Failed(UtcNow),
            nameof(PaymentStatus.Voided) => PaymentTransactionFactory.Voided(UtcNow),
            nameof(PaymentStatus.Refunded) => PaymentTransactionFactory.Refunded(UtcNow),
            _ => throw new InvalidOperationException(statusName),
        };

        // Use the stored gateway-transaction-id (if any) so the append-only guard (I-4) does not
        // shadow the FSM check. Failed state has no stored id, so we send a fresh one.
        var gatewayId = tx.GatewayTransactionId ?? "gw-tx-new";

        // Act
        var action = () => tx.Authorize(gatewayId, PaymentTransactionFactory.SuccessResponse, ExpiresAt, UtcNow);

        // Assert
        action.Should().Throw<DataIntegrityException>()
            .WithMessage("*Invalid payment status transition*");
    }

    [Fact]
    public void Authorize_FromCompleted_ThrowsDataIntegrityException()
    {
        // Arrange
        var tx = PaymentTransactionFactory.Completed(UtcNow);

        // Act
        var action = () => tx.Authorize(
            PaymentTransactionFactory.DefaultGatewayTransactionId,
            PaymentTransactionFactory.SuccessResponse,
            ExpiresAt,
            UtcNow);

        // Assert
        action.Should().Throw<DataIntegrityException>()
            .WithMessage("*Invalid payment status transition*");
    }

    [Fact]
    public void Authorize_WithEmptyGatewayTransactionId_ThrowsArgumentException()
    {
        // Arrange
        var tx = PaymentTransactionFactory.Requested();

        // Act
        var action = () => tx.Authorize("", PaymentTransactionFactory.SuccessResponse, ExpiresAt, UtcNow);

        // Assert
        action.Should().Throw<ArgumentException>();
    }
}
