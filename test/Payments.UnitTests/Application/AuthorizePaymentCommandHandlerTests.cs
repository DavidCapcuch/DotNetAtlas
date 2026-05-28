using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Payments.Application.Abstractions;
using Payments.Application.Common.Data;
using Payments.Application.Transactions.AuthorizePayment;
using Payments.Domain.Errors;
using Payments.Domain.Transactions;
using Payments.Domain.Transactions.Events;
using Payments.Domain.Transactions.ValueObjects;
using Payments.UnitTests.Transactions;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.ValueObjects;

namespace Payments.UnitTests.Application;

public class AuthorizePaymentCommandHandlerTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero));
    private readonly IPaymentRepository _repository = Substitute.For<IPaymentRepository>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly ITransactionalOutbox<IPaymentsDbContext> _outbox = Substitute.For<ITransactionalOutbox<IPaymentsDbContext>>();
    private readonly IDomainEventDispatcher _dispatcher = Substitute.For<IDomainEventDispatcher>();

    private AuthorizePaymentCommandHandler BuildHandler() =>
        new(_repository, _gateway, _outbox, _dispatcher, _timeProvider, NullLogger<AuthorizePaymentCommandHandler>.Instance);

    private static AuthorizePaymentCommand BuildCommand(decimal amount = 100m, string idempotencyKey = "key-1") => new()
    {
        PaymentId = Guid.CreateVersion7(),
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
        _repository.GetByIdForUpdateAsync(command.PaymentId, Arg.Any<CancellationToken>())
            .Returns((PaymentTransaction?)null);
        _gateway.AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new AuthorizeResponse("gw-tx-1", GatewayResponseCode.Create("ok", "Approved"), _timeProvider.GetUtcNow().AddDays(7))));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            _repository.Received(1).Add(Arg.Is<PaymentTransaction>(t =>
                t.Id == command.PaymentId && t.Status == PaymentStatus.Authorized));
            await _gateway.Received(1).AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            await _dispatcher.Received().DispatchAsync(Arg.Any<PaymentRequestedDomainEvent>(), Arg.Any<CancellationToken>());
            await _dispatcher.Received().DispatchAsync(Arg.Any<PaymentAuthorizedDomainEvent>(), Arg.Any<CancellationToken>());
            // H-3: two SaveChanges sites — first persists the Requested aggregate before the
            // gateway call (double-charge anchor), second persists the Authorized transition
            // + outbox rows.
            await _outbox.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_NewPayment_PersistsRequestedBeforeGatewayCall()
    {
        // H-3 ordering pin: the first SaveChangesAsync MUST happen before the gateway is
        // touched. NSubstitute's Received.InOrder block fails if the call sequence differs.
        var command = BuildCommand();
        _repository.GetByIdForUpdateAsync(command.PaymentId, Arg.Any<CancellationToken>())
            .Returns((PaymentTransaction?)null);
        _gateway.AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new AuthorizeResponse("gw-tx-1", GatewayResponseCode.Create("ok", "Approved"), _timeProvider.GetUtcNow().AddDays(7))));

        await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        Received.InOrder(() =>
        {
            _ = _outbox.SaveChangesAsync(Arg.Any<CancellationToken>());
            _ = _gateway.AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            _ = _outbox.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Handle_SagaRetry_AggregateAlreadyInRequested_CallsGatewayOnceAndSavesOnce()
    {
        // H-3 saga-retry scenario: simulates the case where the first attempt's gateway call
        // succeeded but the post-gateway SaveChanges failed and rolled back. The Requested
        // aggregate stayed durable (H-3 anchor); saga retry now finds it via GetByIdForUpdateAsync,
        // skips the Create branch, and proceeds to a single SaveChanges after the gateway.
        var existing = PaymentTransactionFactory.Requested(_timeProvider.GetUtcNow());
        existing.PopDomainEvents();
        var command = BuildCommand();
        _repository.GetByIdForUpdateAsync(command.PaymentId, Arg.Any<CancellationToken>())
            .Returns(existing);
        _gateway.AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new AuthorizeResponse("gw-tx-1", GatewayResponseCode.Create("ok", "Approved"), _timeProvider.GetUtcNow().AddDays(7))));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            existing.Status.Should().Be(PaymentStatus.Authorized);
            _repository.DidNotReceive().Add(Arg.Any<PaymentTransaction>());
            await _gateway.Received(1).AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            // Single SaveChanges because the Requested aggregate is already durable.
            await _outbox.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_GatewayDecline_TransitionsAggregateToFailedAndReturnsOk()
    {
        var command = BuildCommand();
        _repository.GetByIdForUpdateAsync(command.PaymentId, Arg.Any<CancellationToken>())
            .Returns((PaymentTransaction?)null);
        _gateway.AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<AuthorizeResponse>(new GatewayDeclinedError("declined", "insufficient_funds")));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            _repository.Received(1).Add(Arg.Is<PaymentTransaction>(t => t.Status == PaymentStatus.Failed));
            await _dispatcher.Received().DispatchAsync(Arg.Any<PaymentAuthorizationFailedDomainEvent>(), Arg.Any<CancellationToken>());
            await _dispatcher.Received().DispatchAsync(Arg.Any<PaymentFailedDomainEvent>(), Arg.Any<CancellationToken>());
            // H-3: first SaveChanges persists Requested (before gateway), second persists Failed.
            await _outbox.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_GatewayInfrastructureError_ReturnsGatewayUnavailable_AndPersistsRequestedState()
    {
        var command = BuildCommand();
        _repository.GetByIdForUpdateAsync(command.PaymentId, Arg.Any<CancellationToken>())
            .Returns((PaymentTransaction?)null);
        _gateway.AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
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
            await _outbox.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_AggregateAlreadyAuthorized_IsIdempotentNoOp()
    {
        var command = BuildCommand();
        var existing = PaymentTransactionFactory.Authorized(_timeProvider.GetUtcNow());
        _repository.GetByIdForUpdateAsync(command.PaymentId, Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            await _outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
            _repository.DidNotReceive().Add(Arg.Any<PaymentTransaction>());
        }
    }

    [Fact]
    public async Task Handle_InvalidAmount_ReturnsValidationFailureBeforeGateway()
    {
        var command = BuildCommand(amount: 0m);
        _repository.GetByIdForUpdateAsync(command.PaymentId, Arg.Any<CancellationToken>())
            .Returns((PaymentTransaction?)null);

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            await _outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
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
        _repository.GetByIdForUpdateAsync(command.PaymentId, Arg.Any<CancellationToken>())
            .Returns((PaymentTransaction?)null);
        _gateway.AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new AuthorizeResponse("gw-tx-1", GatewayResponseCode.Create("ok", "Approved"), _timeProvider.GetUtcNow().AddDays(7))));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            await _gateway.Received(1).AuthorizeAsync(Arg.Any<PaymentTransaction>(), Key, Arg.Any<CancellationToken>());
        }
    }
}
