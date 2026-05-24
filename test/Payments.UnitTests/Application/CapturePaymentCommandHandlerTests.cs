using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Payments.Application.Abstractions;
using Payments.Application.Common.Data;
using Payments.Application.Transactions.CapturePayment;
using Payments.Domain.Errors;
using Payments.Domain.Transactions;
using Payments.Domain.Transactions.Events;
using Payments.Domain.Transactions.ValueObjects;
using Payments.UnitTests.Transactions;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Payments.UnitTests.Application;

public class CapturePaymentCommandHandlerTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero));
    private readonly IPaymentRepository _repository = Substitute.For<IPaymentRepository>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly ITransactionalOutbox<IPaymentsDbContext> _outbox = Substitute.For<ITransactionalOutbox<IPaymentsDbContext>>();
    private readonly IDomainEventDispatcher _dispatcher = Substitute.For<IDomainEventDispatcher>();

    private CapturePaymentCommandHandler BuildHandler() =>
        new(_repository, _gateway, _outbox, _dispatcher, _timeProvider, NullLogger<CapturePaymentCommandHandler>.Instance);

    private static CapturePaymentCommand BuildCommand(Guid? paymentId = null, string? authorizationId = null) => new()
    {
        PaymentId = paymentId ?? Guid.CreateVersion7(),
        CorrelationId = Guid.CreateVersion7(),
        AuthorizationId = authorizationId ?? PaymentTransactionFactory.DefaultGatewayTransactionId,
    };

    [Fact]
    public async Task Handle_AuthorizedAggregate_HappyPath_CapturesAndCompletes()
    {
        var existing = PaymentTransactionFactory.Authorized(_timeProvider.GetUtcNow());
        var command = BuildCommand(existing.Id, existing.GatewayTransactionId);
        _repository.GetByCorrelationIdForUpdateAsync(command.CorrelationId, Arg.Any<CancellationToken>()).Returns(existing);
        _gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new CaptureResponse(PaymentTransactionFactory.DefaultGatewayTransactionId, GatewayResponseCode.Create("ok", "Captured"))));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            existing.Status.Should().Be(PaymentStatus.Completed);
            await _dispatcher.Received().DispatchAsync(Arg.Any<PaymentCapturedDomainEvent>(), Arg.Any<CancellationToken>());
            await _dispatcher.Received().DispatchAsync(Arg.Any<PaymentCompletedDomainEvent>(), Arg.Any<CancellationToken>());
            await _outbox.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_GatewayDeclineOnCapture_TransitionsToFailedAndReturnsOk()
    {
        var existing = PaymentTransactionFactory.Authorized(_timeProvider.GetUtcNow());
        var command = BuildCommand(existing.Id, existing.GatewayTransactionId);
        _repository.GetByCorrelationIdForUpdateAsync(command.CorrelationId, Arg.Any<CancellationToken>()).Returns(existing);
        _gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<CaptureResponse>(new GatewayDeclinedError("declined", "fraud_suspected")));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            existing.Status.Should().Be(PaymentStatus.Failed);
            await _dispatcher.Received().DispatchAsync(Arg.Any<PaymentCaptureFailedDomainEvent>(), Arg.Any<CancellationToken>());
            await _dispatcher.Received().DispatchAsync(Arg.Any<PaymentFailedDomainEvent>(), Arg.Any<CancellationToken>());
            await _outbox.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_GatewayInfrastructureError_ReturnsGatewayUnavailable()
    {
        var existing = PaymentTransactionFactory.Authorized(_timeProvider.GetUtcNow());
        var command = BuildCommand(existing.Id, existing.GatewayTransactionId);
        _repository.GetByCorrelationIdForUpdateAsync(command.CorrelationId, Arg.Any<CancellationToken>()).Returns(existing);
        _gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<CaptureResponse>(new ValidationError("Gateway", "timeout", "Payments.GatewayUnavailable")));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure().And.HaveError("Payment gateway is temporarily unavailable.");
            existing.Status.Should().Be(PaymentStatus.Authorized);
            await _outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_PaymentNotFound_ReturnsNotFoundError()
    {
        var command = BuildCommand();
        _repository.GetByCorrelationIdForUpdateAsync(command.CorrelationId, Arg.Any<CancellationToken>()).Returns((PaymentTransaction?)null);

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            await _gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
            await _outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_AlreadyCompleted_IsIdempotentNoOp()
    {
        var existing = PaymentTransactionFactory.Completed(_timeProvider.GetUtcNow());
        var command = BuildCommand(existing.Id, existing.GatewayTransactionId);
        _repository.GetByCorrelationIdForUpdateAsync(command.CorrelationId, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            await _gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
            await _outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_VoidedAggregate_FsmRejectsBeforeGatewayCall()
    {
        // H-Cond-2: a Capture issued against a Voided aggregate (saga ordering bug) must throw
        // the FSM source-state guard BEFORE the gateway is contacted — a real PSP would error
        // (or worse, silently re-process) on a Capture against a voided authorization.
        var existing = PaymentTransactionFactory.Voided(_timeProvider.GetUtcNow());
        var command = BuildCommand(existing.Id, existing.GatewayTransactionId);
        _repository.GetByCorrelationIdForUpdateAsync(command.CorrelationId, Arg.Any<CancellationToken>()).Returns(existing);

        var act = async () => await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<DataIntegrityException>();
        thrown.Which.ErrorCode.Should().Be("Payments.InvalidStatusTransition");
        await _gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
        existing.Status.Should().Be(PaymentStatus.Voided);
    }

    [Fact]
    public async Task Handle_AuthorizationIdMismatch_ThrowsAndDoesNotCallGateway()
    {
        // H-8: a wire AuthorizationId that disagrees with the stored GatewayTransactionId
        // is bug-class — must throw before the gateway is touched.
        var existing = PaymentTransactionFactory.Authorized(_timeProvider.GetUtcNow());
        var command = BuildCommand(existing.Id, authorizationId: "wrong-token");
        _repository.GetByCorrelationIdForUpdateAsync(command.CorrelationId, Arg.Any<CancellationToken>()).Returns(existing);

        var act = async () => await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<DataIntegrityException>();
        thrown.Which.ErrorCode.Should().Be("Payments.AuthorizationIdMismatch");
        await _gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
        existing.Status.Should().Be(PaymentStatus.Authorized);
    }
}
