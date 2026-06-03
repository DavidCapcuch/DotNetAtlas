using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Payments.Application.Abstractions;
using Payments.Application.Transactions.RequestRefund;
using Payments.Domain.Transactions.Events;
using Payments.Domain.Transactions.ValueObjects;
using Payments.UnitTests.Application.Common;
using Payments.UnitTests.Transactions;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Payments.UnitTests.Application;

public class RequestRefundCommandHandlerTests : PaymentsHandlerTestBase
{
    private RequestRefundCommandHandler BuildHandler() =>
        new(DbContext, Gateway, Outbox, TimeProvider, NullLogger<RequestRefundCommandHandler>.Instance);

    private static RequestRefundCommand BuildCommand(Guid? paymentId = null) => new()
    {
        PaymentId = paymentId ?? Guid.CreateVersion7(),
        Reason = "saga_compensation",
    };

    [Fact]
    public async Task Handle_CompletedAggregate_HappyPath_TransitionsToRefunded()
    {
        var existing = PaymentTransactionFactory.Completed(TimeProvider.GetUtcNow());
        await SeedAsync(existing);
        var command = BuildCommand(existing.Id);
        Gateway.RefundAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new RefundResponse(GatewayResponseCode.Create("ok", "Refunded"))));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            existing.Status.Should().Be(PaymentStatus.Refunded);
            // Dispatch is owned by DispatchDomainEventsInterceptor (ADR-0024); verify the aggregate
            // raised the event that the interceptor will pop on SaveChanges.
            existing.PopDomainEvents().Should().ContainSingle().Which.Should().BeOfType<PaymentRefundedDomainEvent>();
            await Outbox.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_GatewayFailure_ReturnsGatewayUnavailable()
    {
        var existing = PaymentTransactionFactory.Completed(TimeProvider.GetUtcNow());
        await SeedAsync(existing);
        var command = BuildCommand(existing.Id);
        Gateway.RefundAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<RefundResponse>("gateway-error"));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            existing.Status.Should().Be(PaymentStatus.Completed);
            await Outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_AlreadyRefunded_IsIdempotentNoOp()
    {
        var existing = PaymentTransactionFactory.Refunded(TimeProvider.GetUtcNow());
        await SeedAsync(existing);
        var command = BuildCommand(existing.Id);

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            await Gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            await Outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_PaymentNotFound_ReturnsFailure()
    {
        var command = BuildCommand();

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        result.Should().BeFailure();
    }

    [Fact]
    public async Task Handle_AuthorizedAggregate_FsmRejectsBeforeGatewayCall()
    {
        // H-Cond-2: a Refund issued against an Authorized (not-yet-Captured) aggregate must
        // throw the FSM source-state guard BEFORE the gateway is contacted — a real PSP would
        // reject the refund or, worse, double-process. The Refund/Void asymmetry is a saga bug.
        var existing = PaymentTransactionFactory.Authorized(TimeProvider.GetUtcNow());
        await SeedAsync(existing);
        var command = BuildCommand(existing.Id);

        var act = async () => await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<DataIntegrityException>();
        thrown.Which.ErrorCode.Should().Be("Payments.InvalidStatusTransition");
        await Gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        existing.Status.Should().Be(PaymentStatus.Authorized);
    }
}
