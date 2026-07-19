using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Payments.Application.Abstractions;
using Payments.Application.Transactions.CapturePayment;
using Payments.Domain.Errors;
using Payments.Domain.Transactions.Events;
using Payments.Domain.Transactions.ValueObjects;
using Payments.UnitTests.Application.Common;
using Payments.UnitTests.Transactions;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Payments.UnitTests.Application;

public class CapturePaymentCommandHandlerTests : PaymentsHandlerTestBase
{
    private CapturePaymentCommandHandler BuildHandler() =>
        new(DbContext, Gateway, Outbox, TimeProvider, NullLogger<CapturePaymentCommandHandler>.Instance);

    private static CapturePaymentCommand BuildCommand(
        Guid? orderId = null,
        string? authorizationId = null) => new()
        {
            OrderId = orderId ?? Guid.CreateVersion7(),
            AuthorizationId = authorizationId ?? PaymentTransactionFactory.DefaultGatewayTransactionId,
        };

    [Fact]
    public async Task Handle_AuthorizedAggregate_HappyPath_CapturesAndCompletes()
    {
        // Arrange
        var existing = PaymentTransactionFactory.Authorized(TimeProvider.GetUtcNow());
        await SeedAsync(existing);
        var command = BuildCommand(existing.OrderId, existing.GatewayTransactionId);
        Gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new CaptureResponse(PaymentTransactionFactory.DefaultGatewayTransactionId, GatewayResponseCode.Create("ok", "Captured"))));

        // Act
        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            existing.Status.Should().Be(PaymentStatus.Completed);
            // Dispatch is owned by DispatchDomainEventsInterceptor (ADR-0024); verify the aggregate
            // raised both events that the interceptor will pop on SaveChanges.
            var raised = existing.PopDomainEvents();
            raised.Should().Contain(e => e is PaymentCapturedDomainEvent);
            raised.Should().Contain(e => e is PaymentCompletedDomainEvent);
            await Outbox.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_GatewayDeclineOnCapture_TransitionsToFailedAndReturnsOk()
    {
        // Arrange
        var existing = PaymentTransactionFactory.Authorized(TimeProvider.GetUtcNow());
        await SeedAsync(existing);
        var command = BuildCommand(existing.OrderId, existing.GatewayTransactionId);
        Gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<CaptureResponse>(new GatewayDeclinedError("declined", "fraud_suspected")));

        // Act
        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            existing.Status.Should().Be(PaymentStatus.Failed);
            // Dispatch is owned by DispatchDomainEventsInterceptor (ADR-0024); verify the aggregate
            // raised both events that the interceptor will pop on SaveChanges.
            var raised = existing.PopDomainEvents();
            raised.Should().Contain(e => e is PaymentCaptureFailedDomainEvent);
            raised.Should().Contain(e => e is PaymentFailedDomainEvent);
            await Outbox.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task Handle_GatewayInfrastructureError_ReturnsGatewayUnavailable()
    {
        // Arrange
        var existing = PaymentTransactionFactory.Authorized(TimeProvider.GetUtcNow());
        await SeedAsync(existing);
        var command = BuildCommand(existing.OrderId, existing.GatewayTransactionId);
        Gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<CaptureResponse>(new ValidationError("Gateway", "timeout", "Payments.GatewayUnavailable")));

        // Act
        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((Platform.SharedKernel.Errors.DomainError)e).ErrorCode == "Payments.GatewayUnavailable");
            existing.Status.Should().Be(PaymentStatus.Authorized);
            await Outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_PaymentNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var command = BuildCommand();

        // Act
        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            await Gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
            await Outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_AlreadyCompleted_IsIdempotentNoOp()
    {
        // Arrange
        var existing = PaymentTransactionFactory.Completed(TimeProvider.GetUtcNow());
        await SeedAsync(existing);
        var command = BuildCommand(existing.OrderId, existing.GatewayTransactionId);

        // Act
        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            await Gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
            await Outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_VoidedAggregate_FsmRejectsBeforeGatewayCall()
    {
        // Arrange
        // H-Cond-2: a Capture issued against a Voided aggregate (saga ordering bug) must throw
        // the FSM source-state guard BEFORE the gateway is contacted — a real PSP would error
        // (or worse, silently re-process) on a Capture against a voided authorization.
        var existing = PaymentTransactionFactory.Voided(TimeProvider.GetUtcNow());
        await SeedAsync(existing);
        var command = BuildCommand(existing.OrderId, existing.GatewayTransactionId);

        // Act
        var act = async () => await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            var thrown = await act.Should().ThrowAsync<DataIntegrityException>();
            thrown.Which.ErrorCode.Should().Be("Payments.InvalidStatusTransition");
            await Gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
            existing.Status.Should().Be(PaymentStatus.Voided);
        }
    }

    [Fact]
    public async Task Handle_AuthorizationIdMismatch_ThrowsAndDoesNotCallGateway()
    {
        // Arrange
        // A wire AuthorizationId that disagrees with the stored GatewayTransactionId
        // is bug-class — must throw before the gateway is touched.
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
            await Gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
            existing.Status.Should().Be(PaymentStatus.Authorized);
        }
    }
}
