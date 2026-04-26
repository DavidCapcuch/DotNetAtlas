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

    private static AuthorizePaymentCommand BuildCommand(decimal amount = 100m) => new()
    {
        PaymentId = Guid.CreateVersion7(),
        CorrelationId = Guid.CreateVersion7(),
        BuyerId = Guid.CreateVersion7(),
        OrderId = Guid.CreateVersion7(),
        Amount = amount,
        Currency = "USD",
        PaymentMethodId = "tok_visa_4242",
    };

    [Fact]
    public async Task Handle_NewPayment_HappyPath_CreatesAuthorizesAndSaves()
    {
        var command = BuildCommand();
        _repository.GetByIdAsync(command.PaymentId, Arg.Any<CancellationToken>())
            .Returns((PaymentTransaction?)null);
        _gateway.AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new AuthorizeResponse("gw-tx-1", new GatewayResponseCode("ok", "Approved"))));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            _repository.Received(1).Add(Arg.Is<PaymentTransaction>(t =>
                t.Id == command.PaymentId && t.Status == PaymentStatus.Authorized));
            await _gateway.Received(1).AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<CancellationToken>());
            await _dispatcher.Received().DispatchAsync(Arg.Any<PaymentRequestedDomainEvent>(), Arg.Any<CancellationToken>());
            await _dispatcher.Received().DispatchAsync(Arg.Any<PaymentAuthorizedDomainEvent>(), Arg.Any<CancellationToken>());
            await _outbox.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_GatewayDecline_TransitionsAggregateToFailedAndReturnsOk()
    {
        var command = BuildCommand();
        _repository.GetByIdAsync(command.PaymentId, Arg.Any<CancellationToken>())
            .Returns((PaymentTransaction?)null);
        _gateway.AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<AuthorizeResponse>(new GatewayDeclinedError("declined", "insufficient_funds")));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            _repository.Received(1).Add(Arg.Is<PaymentTransaction>(t => t.Status == PaymentStatus.Failed));
            await _dispatcher.Received().DispatchAsync(Arg.Any<PaymentAuthorizationFailedDomainEvent>(), Arg.Any<CancellationToken>());
            await _dispatcher.Received().DispatchAsync(Arg.Any<PaymentFailedDomainEvent>(), Arg.Any<CancellationToken>());
            await _outbox.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_GatewayInfrastructureError_ReturnsGatewayUnavailableAndDoesNotSave()
    {
        var command = BuildCommand();
        _repository.GetByIdAsync(command.PaymentId, Arg.Any<CancellationToken>())
            .Returns((PaymentTransaction?)null);
        _gateway.AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<AuthorizeResponse>(new ValidationError("Gateway", "timeout", "Payments.GatewayUnavailable")));

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure().And.HaveError("Payment gateway is temporarily unavailable.");
            await _outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_AggregateAlreadyAuthorized_IsIdempotentNoOp()
    {
        var command = BuildCommand();
        var existing = PaymentTransactionFactory.Authorized(_timeProvider.GetUtcNow());
        _repository.GetByIdAsync(command.PaymentId, Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<CancellationToken>());
            await _outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
            _repository.DidNotReceive().Add(Arg.Any<PaymentTransaction>());
        }
    }

    [Fact]
    public async Task Handle_InvalidAmount_ReturnsValidationFailureBeforeGateway()
    {
        var command = BuildCommand(amount: 0m);
        _repository.GetByIdAsync(command.PaymentId, Arg.Any<CancellationToken>())
            .Returns((PaymentTransaction?)null);

        var result = await BuildHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<CancellationToken>());
            await _outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
