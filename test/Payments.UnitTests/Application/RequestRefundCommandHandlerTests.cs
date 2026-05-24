using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Payments.Application.Abstractions;
using Payments.Application.Common.Data;
using Payments.Application.Transactions.RequestRefund;
using Payments.Domain.Transactions;
using Payments.Domain.Transactions.Events;
using Payments.Domain.Transactions.ValueObjects;
using Payments.UnitTests.Transactions;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Payments.UnitTests.Application;

public class RequestRefundCommandHandlerTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero));
    private readonly IPaymentRepository _repository = Substitute.For<IPaymentRepository>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly ITransactionalOutbox<IPaymentsDbContext> _outbox = Substitute.For<ITransactionalOutbox<IPaymentsDbContext>>();
    private readonly IDomainEventDispatcher _dispatcher = Substitute.For<IDomainEventDispatcher>();

    private RequestRefundCommandHandler BuildHandler() =>
        new(_repository, _gateway, _outbox, _dispatcher, _timeProvider, NullLogger<RequestRefundCommandHandler>.Instance);

    private static RequestRefundCommand BuildCommand(Guid? paymentId = null) => new()
    {
        PaymentId = paymentId ?? Guid.CreateVersion7(),
        CorrelationId = Guid.CreateVersion7(),
        Reason = "saga_compensation",
    };

    [Fact]
    public async Task Handle_CompletedAggregate_HappyPath_TransitionsToRefunded()
    {
        var existing = PaymentTransactionFactory.Completed(_timeProvider.GetUtcNow());
        var command = BuildCommand(existing.Id);
        _repository.GetByCorrelationIdForUpdateAsync(command.CorrelationId, Arg.Any<CancellationToken>()).Returns(existing);
        _gateway.RefundAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new RefundResponse(GatewayResponseCode.Create("ok", "Refunded"))));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            existing.Status.Should().Be(PaymentStatus.Refunded);
            await _dispatcher.Received().DispatchAsync(Arg.Any<PaymentRefundedDomainEvent>(), Arg.Any<CancellationToken>());
            await _outbox.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_GatewayFailure_ReturnsGatewayUnavailable()
    {
        var existing = PaymentTransactionFactory.Completed(_timeProvider.GetUtcNow());
        var command = BuildCommand(existing.Id);
        _repository.GetByCorrelationIdForUpdateAsync(command.CorrelationId, Arg.Any<CancellationToken>()).Returns(existing);
        _gateway.RefundAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<RefundResponse>("gateway-error"));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            existing.Status.Should().Be(PaymentStatus.Completed);
            await _outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_AlreadyRefunded_IsIdempotentNoOp()
    {
        var existing = PaymentTransactionFactory.Refunded(_timeProvider.GetUtcNow());
        var command = BuildCommand(existing.Id);
        _repository.GetByCorrelationIdForUpdateAsync(command.CorrelationId, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            await _outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_PaymentNotFound_ReturnsFailure()
    {
        var command = BuildCommand();
        _repository.GetByCorrelationIdForUpdateAsync(command.CorrelationId, Arg.Any<CancellationToken>()).Returns((PaymentTransaction?)null);

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        result.Should().BeFailure();
    }

    [Fact]
    public async Task Handle_AuthorizedAggregate_FsmRejectsBeforeGatewayCall()
    {
        // H-Cond-2: a Refund issued against an Authorized (not-yet-Captured) aggregate must
        // throw the FSM source-state guard BEFORE the gateway is contacted — a real PSP would
        // reject the refund or, worse, double-process. The Refund/Void asymmetry is a saga bug.
        var existing = PaymentTransactionFactory.Authorized(_timeProvider.GetUtcNow());
        var command = BuildCommand(existing.Id);
        _repository.GetByCorrelationIdForUpdateAsync(command.CorrelationId, Arg.Any<CancellationToken>()).Returns(existing);

        var act = async () => await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<DataIntegrityException>();
        thrown.Which.ErrorCode.Should().Be("Payments.InvalidStatusTransition");
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        existing.Status.Should().Be(PaymentStatus.Authorized);
    }
}
