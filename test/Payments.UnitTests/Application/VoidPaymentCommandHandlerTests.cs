using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Payments.Application.Abstractions;
using Payments.Application.Transactions.VoidPayment;
using Payments.Domain.Transactions.Events;
using Payments.Domain.Transactions.ValueObjects;
using Payments.UnitTests.Application.Common;
using Payments.UnitTests.Transactions;
using Platform.SharedKernel.Exceptions;

namespace Payments.UnitTests.Application;

public class VoidPaymentCommandHandlerTests : PaymentsHandlerTestBase
{
    private VoidPaymentCommandHandler BuildHandler() =>
        new(DbContext, Gateway, Outbox, TimeProvider, NullLogger<VoidPaymentCommandHandler>.Instance);

    private static VoidPaymentCommand BuildCommand(
        Guid? orderId = null,
        string? authorizationId = null) => new()
        {
            OrderId = orderId ?? Guid.CreateVersion7(),
            AuthorizationId = authorizationId ?? PaymentTransactionFactory.DefaultGatewayTransactionId,
            Reason = "saga_compensation",
        };

    [Fact]
    public async Task Handle_AuthorizedAggregate_HappyPath_TransitionsToVoided()
    {
        // Arrange
        var existing = PaymentTransactionFactory.Authorized(TimeProvider.GetUtcNow());
        await SeedAsync(existing);
        var command = BuildCommand(existing.OrderId, existing.GatewayTransactionId);
        Gateway.VoidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new VoidResponse(GatewayResponseCode.Create("ok", "Voided"))));

        // Act
        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            existing.Status.Should().Be(PaymentStatus.Voided);
            // Dispatch is owned by DispatchDomainEventsInterceptor (ADR-0024); verify the aggregate
            // raised the event that the interceptor will pop on SaveChanges.
            existing.PopDomainEvents().Should().ContainSingle().Which.Should().BeOfType<PaymentVoidedDomainEvent>();
            await Outbox.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task Handle_GatewayFailure_ReturnsGatewayUnavailable()
    {
        // Arrange
        var existing = PaymentTransactionFactory.Authorized(TimeProvider.GetUtcNow());
        await SeedAsync(existing);
        var command = BuildCommand(existing.OrderId, existing.GatewayTransactionId);
        Gateway.VoidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<VoidResponse>("infra-error"));

        // Act
        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            existing.Status.Should().Be(PaymentStatus.Authorized);
            await Outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_AlreadyVoided_IsIdempotentNoOp()
    {
        // Arrange
        var existing = PaymentTransactionFactory.Voided(TimeProvider.GetUtcNow());
        await SeedAsync(existing);
        var command = BuildCommand(existing.OrderId, existing.GatewayTransactionId);

        // Act
        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            await Gateway.DidNotReceive().VoidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            await Outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_PaymentNotFound_ReturnsFailure()
    {
        // Arrange
        var command = BuildCommand();

        // Act
        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailure();
    }

    [Fact]
    public async Task Handle_CompletedAggregate_FsmRejectsBeforeGatewayCall()
    {
        // Arrange
        // H-Cond-2: a Void issued against a Completed aggregate (saga ordering bug) must
        // throw the FSM source-state guard BEFORE the gateway is contacted — a real PSP
        // would otherwise see a Void on an already-captured authorization (undefined behaviour).
        var existing = PaymentTransactionFactory.Completed(TimeProvider.GetUtcNow());
        await SeedAsync(existing);
        var command = BuildCommand(existing.OrderId, existing.GatewayTransactionId);

        // Act
        var act = async () => await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            var thrown = await act.Should().ThrowAsync<DataIntegrityException>();
            thrown.Which.ErrorCode.Should().Be("Payments.InvalidStatusTransition");
            await Gateway.DidNotReceive().VoidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            existing.Status.Should().Be(PaymentStatus.Completed);
        }
    }

    [Fact]
    public async Task Handle_AuthorizationIdMismatch_ThrowsAndDoesNotCallGateway()
    {
        // Arrange
        // A wire AuthorizationId that disagrees with the stored GatewayTransactionId
        // is bug-class (stale-token replay / saga bug). Must throw before the gateway is touched
        // so the message routes to DLT for operator inspection.
        var existing = PaymentTransactionFactory.Authorized(TimeProvider.GetUtcNow());
        await SeedAsync(existing);
        var command = BuildCommand(existing.OrderId, authorizationId: "wrong-token");

        // Act
        var act = async () => await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            var thrown = await act.Should().ThrowAsync<DataIntegrityException>();
            thrown.Which.ErrorCode.Should().Be("Payments.AuthorizationIdMismatch");
            await Gateway.DidNotReceive().VoidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            existing.Status.Should().Be(PaymentStatus.Authorized);
        }
    }
}
