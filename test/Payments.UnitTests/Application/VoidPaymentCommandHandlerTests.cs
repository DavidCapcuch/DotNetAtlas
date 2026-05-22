using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Payments.Application.Abstractions;
using Payments.Application.Common.Data;
using Payments.Application.Transactions.VoidPayment;
using Payments.Domain.Transactions;
using Payments.Domain.Transactions.Events;
using Payments.Domain.Transactions.ValueObjects;
using Payments.UnitTests.Transactions;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Exceptions;

namespace Payments.UnitTests.Application;

public class VoidPaymentCommandHandlerTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero));
    private readonly IPaymentRepository _repository = Substitute.For<IPaymentRepository>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly ITransactionalOutbox<IPaymentsDbContext> _outbox = Substitute.For<ITransactionalOutbox<IPaymentsDbContext>>();
    private readonly IDomainEventDispatcher _dispatcher = Substitute.For<IDomainEventDispatcher>();

    private VoidPaymentCommandHandler BuildHandler() =>
        new(_repository, _gateway, _outbox, _dispatcher, _timeProvider, NullLogger<VoidPaymentCommandHandler>.Instance);

    private static VoidPaymentCommand BuildCommand(Guid? paymentId = null, string? authorizationId = null) => new()
    {
        PaymentId = paymentId ?? Guid.CreateVersion7(),
        CorrelationId = Guid.CreateVersion7(),
        AuthorizationId = authorizationId ?? PaymentTransactionFactory.DefaultGatewayTransactionId,
        Reason = "saga_compensation",
    };

    [Fact]
    public async Task Handle_AuthorizedAggregate_HappyPath_TransitionsToVoided()
    {
        var existing = PaymentTransactionFactory.Authorized(_timeProvider.GetUtcNow());
        var command = BuildCommand(existing.Id, existing.GatewayTransactionId);
        _repository.GetByIdAsync(command.PaymentId, Arg.Any<CancellationToken>()).Returns(existing);
        _gateway.VoidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new VoidResponse(new GatewayResponseCode("ok", "Voided"))));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            existing.Status.Should().Be(PaymentStatus.Voided);
            await _dispatcher.Received().DispatchAsync(Arg.Any<PaymentVoidedDomainEvent>(), Arg.Any<CancellationToken>());
            await _outbox.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_GatewayFailure_ReturnsGatewayUnavailable()
    {
        var existing = PaymentTransactionFactory.Authorized(_timeProvider.GetUtcNow());
        var command = BuildCommand(existing.Id, existing.GatewayTransactionId);
        _repository.GetByIdAsync(command.PaymentId, Arg.Any<CancellationToken>()).Returns(existing);
        _gateway.VoidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<VoidResponse>("infra-error"));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            existing.Status.Should().Be(PaymentStatus.Authorized);
            await _outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_AlreadyVoided_IsIdempotentNoOp()
    {
        var existing = PaymentTransactionFactory.Voided(_timeProvider.GetUtcNow());
        var command = BuildCommand(existing.Id, existing.GatewayTransactionId);
        _repository.GetByIdAsync(command.PaymentId, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            await _gateway.DidNotReceive().VoidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            await _outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_PaymentNotFound_ReturnsFailure()
    {
        var command = BuildCommand();
        _repository.GetByIdAsync(command.PaymentId, Arg.Any<CancellationToken>()).Returns((PaymentTransaction?)null);

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        result.Should().BeFailure();
    }

    [Fact]
    public async Task Handle_CompletedAggregate_FsmRejectsBeforeGatewayCall()
    {
        // H-Cond-2: a Void issued against a Completed aggregate (saga ordering bug) must
        // throw the FSM source-state guard BEFORE the gateway is contacted — a real PSP
        // would otherwise see a Void on an already-captured authorization (undefined behaviour).
        var existing = PaymentTransactionFactory.Completed(_timeProvider.GetUtcNow());
        var command = BuildCommand(existing.Id, existing.GatewayTransactionId);
        _repository.GetByIdAsync(command.PaymentId, Arg.Any<CancellationToken>()).Returns(existing);

        var act = async () => await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<DataIntegrityException>();
        thrown.Which.ErrorCode.Should().Be("Payments.InvalidStatusTransition");
        await _gateway.DidNotReceive().VoidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        existing.Status.Should().Be(PaymentStatus.Completed);
    }

    [Fact]
    public async Task Handle_AuthorizationIdMismatch_ThrowsAndDoesNotCallGateway()
    {
        // H-8: a wire AuthorizationId that disagrees with the stored GatewayTransactionId
        // is bug-class (stale-token replay / saga bug). Must throw before the gateway is touched
        // so the message routes to DLT for operator inspection.
        var existing = PaymentTransactionFactory.Authorized(_timeProvider.GetUtcNow());
        var command = BuildCommand(existing.Id, authorizationId: "wrong-token");
        _repository.GetByIdAsync(command.PaymentId, Arg.Any<CancellationToken>()).Returns(existing);

        var act = async () => await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<DataIntegrityException>();
        thrown.Which.ErrorCode.Should().Be("Payments.AuthorizationIdMismatch");
        await _gateway.DidNotReceive().VoidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        existing.Status.Should().Be(PaymentStatus.Authorized);
    }
}
