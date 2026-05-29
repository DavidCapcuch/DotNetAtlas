using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Payments.Application.Abstractions;
using Payments.Application.Transactions.AuthorizePayment;
using Payments.Domain.Errors;
using Payments.Domain.Transactions;
using Payments.Domain.Transactions.Events;
using Payments.Domain.Transactions.ValueObjects;
using Payments.UnitTests.Application.Common;
using Payments.UnitTests.Transactions;
using Platform.SharedKernel.Errors;

namespace Payments.UnitTests.Application;

public class AuthorizePaymentCommandHandlerTests : PaymentsHandlerTestBase
{
    private AuthorizePaymentCommandHandler BuildHandler() =>
        new(DbContext, Gateway, Outbox, Dispatcher, TimeProvider, NullLogger<AuthorizePaymentCommandHandler>.Instance);

    private static AuthorizePaymentCommand BuildCommand(
        decimal amount = 100m,
        string idempotencyKey = "key-1",
        Guid? paymentId = null) => new()
        {
            PaymentId = paymentId ?? Guid.CreateVersion7(),
            CorrelationId = Guid.CreateVersion7(),
            BuyerId = Guid.CreateVersion7(),
            OrderId = Guid.CreateVersion7(),
            Amount = amount,
            Currency = "USD",
            PaymentMethodId = "tok_visa_4242",
            IdempotencyKey = idempotencyKey,
        };

    [Fact]
    public async Task Handle_NewPayment_HappyPath_CreatesAuthorizesAndSaves()
    {
        var command = BuildCommand();
        Gateway.AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new AuthorizeResponse("gw-tx-1", GatewayResponseCode.Create("ok", "Approved"), TimeProvider.GetUtcNow().AddDays(7))));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            DbContext.Transactions.Local.Should().ContainSingle(t =>
                t.Id == command.PaymentId && t.Status == PaymentStatus.Authorized);
            await Gateway.Received(1).AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            await Dispatcher.Received().DispatchAsync(Arg.Any<PaymentRequestedDomainEvent>(), Arg.Any<CancellationToken>());
            await Dispatcher.Received().DispatchAsync(Arg.Any<PaymentAuthorizedDomainEvent>(), Arg.Any<CancellationToken>());
            // H-3: two SaveChanges sites — first persists the Requested aggregate before the
            // gateway call (double-charge anchor), second persists the Authorized transition
            // + outbox rows.
            await Outbox.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_NewPayment_PersistsRequestedBeforeGatewayCall()
    {
        // H-3 ordering pin: the first SaveChangesAsync MUST happen before the gateway is
        // touched. NSubstitute's Received.InOrder block fails if the call sequence differs.
        var command = BuildCommand();
        Gateway.AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new AuthorizeResponse("gw-tx-1", GatewayResponseCode.Create("ok", "Approved"), TimeProvider.GetUtcNow().AddDays(7))));

        await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        Received.InOrder(() =>
        {
            _ = Outbox.SaveChangesAsync(Arg.Any<CancellationToken>());
            _ = Gateway.AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            _ = Outbox.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Handle_SagaRetry_AggregateAlreadyInRequested_CallsGatewayOnceAndSavesOnce()
    {
        // H-3 saga-retry scenario: simulates the case where the first attempt's gateway call
        // succeeded but the post-gateway SaveChanges failed and rolled back. The Requested
        // aggregate stayed durable (H-3 anchor); saga retry now finds it by PK, skips the
        // Create branch, and proceeds to a single SaveChanges after the gateway.
        var existing = PaymentTransactionFactory.Requested(TimeProvider.GetUtcNow());
        existing.PopDomainEvents();
        await SeedAsync(existing);
        var command = BuildCommand(paymentId: existing.Id);
        Gateway.AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new AuthorizeResponse("gw-tx-1", GatewayResponseCode.Create("ok", "Approved"), TimeProvider.GetUtcNow().AddDays(7))));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            existing.Status.Should().Be(PaymentStatus.Authorized);
            // No new aggregate added — the retry reused the durable Requested row.
            DbContext.Transactions.Local.Should().ContainSingle();
            await Gateway.Received(1).AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            // Single SaveChanges because the Requested aggregate is already durable.
            await Outbox.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_GatewayDecline_TransitionsAggregateToFailedAndReturnsOk()
    {
        var command = BuildCommand();
        Gateway.AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<AuthorizeResponse>(new GatewayDeclinedError("declined", "insufficient_funds")));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            DbContext.Transactions.Local.Should().ContainSingle(t => t.Status == PaymentStatus.Failed);
            await Dispatcher.Received().DispatchAsync(Arg.Any<PaymentAuthorizationFailedDomainEvent>(), Arg.Any<CancellationToken>());
            await Dispatcher.Received().DispatchAsync(Arg.Any<PaymentFailedDomainEvent>(), Arg.Any<CancellationToken>());
            // H-3: first SaveChanges persists Requested (before gateway), second persists Failed.
            await Outbox.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_GatewayInfrastructureError_ReturnsGatewayUnavailable_AndPersistsRequestedState()
    {
        var command = BuildCommand();
        Gateway.AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<AuthorizeResponse>(new ValidationError("Gateway", "timeout", "Payments.GatewayUnavailable")));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((Platform.SharedKernel.Errors.DomainError)e).ErrorCode == "Payments.GatewayUnavailable");
            // H-3: the Requested aggregate IS persisted before the gateway call, so saga retry
            // re-enters via the existing-row branch and does not re-create. The Authorized /
            // Failed transition's second SaveChanges does not happen (handler returns early
            // on infrastructure error).
            await Outbox.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_AggregateAlreadyAuthorized_IsIdempotentNoOp()
    {
        var existing = PaymentTransactionFactory.Authorized(TimeProvider.GetUtcNow());
        await SeedAsync(existing);
        var command = BuildCommand(paymentId: existing.Id);

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            await Gateway.DidNotReceive().AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            await Outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
            // No new aggregate added — the already-Authorized row short-circuits.
            DbContext.Transactions.Local.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task Handle_InvalidAmount_ReturnsValidationFailureBeforeGateway()
    {
        var command = BuildCommand(amount: 0m);

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            await Gateway.DidNotReceive().AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            await Outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_NewPayment_PropagatesIdempotencyKeyToGateway()
    {
        // H-4: the saga-issued idempotency key MUST reach IPaymentGateway.AuthorizeAsync so a
        // real PSP adapter can forward it as the gateway's Idempotency-Key header. Verifies
        // the wire field is no longer dropped (was: AuthorizePaymentCommand.IdempotencyKey
        // documented in schema but ignored).
        const string Key = "saga-key-123";
        var command = BuildCommand(idempotencyKey: Key);
        Gateway.AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new AuthorizeResponse("gw-tx-1", GatewayResponseCode.Create("ok", "Approved"), TimeProvider.GetUtcNow().AddDays(7))));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            await Gateway.Received(1).AuthorizeAsync(Arg.Any<PaymentTransaction>(), Key, Arg.Any<CancellationToken>());
        }
    }
}
