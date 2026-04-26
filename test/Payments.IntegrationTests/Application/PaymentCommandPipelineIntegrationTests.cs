using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Payments.Application.Abstractions;
using Payments.Application.Common;
using Payments.Application.Common.Data;
using Payments.Application.Common.Messaging;
using Payments.Application.Transactions.AuthorizePayment;
using Payments.Application.Transactions.CapturePayment;
using Payments.Domain.Transactions;
using Payments.Domain.Transactions.ValueObjects;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.ValueObjects;

namespace Payments.IntegrationTests.Application;

/// <summary>
/// End-to-end pipeline test exercising the full <c>AddPaymentsApplication()</c> DI graph —
/// validation behaviour, CQRS handler, in-process domain-event dispatch, outbox publishers,
/// and the Avro mappers — against NSubstitute mocks for the seam ports
/// (<c>IPaymentRepository</c>, <c>IPaymentGateway</c>, <c>ITransactionalOutbox</c>). Mirrors
/// the basket M4 integration-test shape because the concrete Postgres <c>PaymentsDbContext</c>
/// is an M5 deliverable and not yet available.
/// </summary>
public sealed class PaymentCommandPipelineIntegrationTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 4, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _provider;
    private readonly IPaymentRepository _repository;
    private readonly IPaymentGateway _gateway;
    private readonly ITransactionalOutbox<IPaymentsDbContext> _outbox;

    public PaymentCommandPipelineIntegrationTests()
    {
        _repository = Substitute.For<IPaymentRepository>();
        _gateway = Substitute.For<IPaymentGateway>();
        _outbox = Substitute.For<ITransactionalOutbox<IPaymentsDbContext>>();
        _outbox.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));

        services.AddPaymentsApplication();
        services.Configure<PaymentsTopicsOptions>(opts =>
        {
            opts.Transactions = "payments.transactions";
            opts.DltTopicSuffix = ".DLT";
        });

        services.AddSingleton(_repository);
        services.AddSingleton(_gateway);
        services.AddSingleton(_outbox);

        _provider = services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public async Task AuthorizeThenCapture_HappyPath_EnqueuesAuthorizedAndCapturedEvents()
    {
        var paymentId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        const string PaymentMethodToken = "tok_visa_4242";
        const string GatewayTransactionId = "stub-deadbeef";

        // Authorize: aggregate doesn't exist yet → handler creates + authorizes.
        _repository.GetByIdAsync(paymentId, Arg.Any<CancellationToken>())
            .Returns((PaymentTransaction?)null);
        _gateway.AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new AuthorizeResponse(GatewayTransactionId, new GatewayResponseCode("ok", "Approved"))));

        // Capture the aggregate that the handler will add, so the next step can return it.
        PaymentTransaction? authorizedAggregate = null;
        _repository.When(r => r.Add(Arg.Any<PaymentTransaction>()))
            .Do(call => authorizedAggregate = call.Arg<PaymentTransaction>());

        using var scope = _provider.CreateScope();
        var authorizeHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<AuthorizePaymentCommand, Guid>>();

        var authorizeResult = await authorizeHandler.HandleAsync(
            new AuthorizePaymentCommand
            {
                PaymentId = paymentId,
                CorrelationId = correlationId,
                BuyerId = buyerId,
                OrderId = orderId,
                Amount = 100m,
                Currency = "USD",
                PaymentMethodId = PaymentMethodToken,
            },
            TestContext.Current.CancellationToken);

        authorizedAggregate.Should().NotBeNull();

        // Configure repo to return the (now Authorized) aggregate for the capture step.
        _repository.GetByIdAsync(paymentId, Arg.Any<CancellationToken>())
            .Returns(authorizedAggregate);
        _gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new CaptureResponse(GatewayTransactionId, new GatewayResponseCode("ok", "Captured"))));

        var captureHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CapturePaymentCommand>>();

        var captureResult = await captureHandler.HandleAsync(
            new CapturePaymentCommand { PaymentId = paymentId, CorrelationId = correlationId },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            authorizeResult.Should().BeSuccess();
            captureResult.Should().BeSuccess();
            authorizedAggregate!.Status.Should().Be(PaymentStatus.Completed);

            // Verify the wire-shape Avro events landed on the outbox with correct topic + key + payload.
            _outbox.Received(1).AddOutboxMessage(
                Arg.Is<string>(t => t == "payments.transactions"),
                Arg.Is<string>(k => k == correlationId.ToString()),
                Arg.Is<global::Payments.Transactions.PaymentAuthorizedEvent>(e =>
                    e.CorrelationId == correlationId
                    && e.UserId == buyerId
                    && e.AuthorizationId == GatewayTransactionId
                    && e.Currency == "USD"));

            _outbox.Received(1).AddOutboxMessage(
                Arg.Is<string>(t => t == "payments.transactions"),
                Arg.Is<string>(k => k == correlationId.ToString()),
                Arg.Is<global::Payments.Transactions.PaymentCapturedEvent>(e =>
                    e.CorrelationId == correlationId
                    && e.UserId == buyerId
                    && e.PaymentTransactionId == paymentId
                    && e.AuthorizationId == GatewayTransactionId
                    && e.Currency == "USD"));

            // Outbox.SaveChangesAsync called once per command (exactly two times across both commands).
            await _outbox.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task AuthorizeRetry_AggregateAlreadyAuthorized_IsIdempotentNoOpAndDoesNotCallGateway()
    {
        var paymentId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();

        // Pre-built aggregate already in Authorized status: simulates the saga retry case
        // (Example 2.2 in docs/bc-design/example-mapping/payments.md).
        var existing = BuildAuthorizedAggregate();
        _repository.GetByIdAsync(paymentId, Arg.Any<CancellationToken>())
            .Returns(existing);

        using var scope = _provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<AuthorizePaymentCommand, Guid>>();

        var result = await handler.HandleAsync(
            new AuthorizePaymentCommand
            {
                PaymentId = paymentId,
                CorrelationId = correlationId,
                BuyerId = buyerId,
                OrderId = orderId,
                Amount = 100m,
                Currency = "USD",
                PaymentMethodId = "tok_visa_4242",
            },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<CancellationToken>());
            _outbox.DidNotReceive().AddOutboxMessage(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<global::Avro.Specific.ISpecificRecord>());
            await _outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task AuthorizeWithInvalidAmount_FailsValidationBehavior_BeforeReachingHandler()
    {
        using var scope = _provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<AuthorizePaymentCommand, Guid>>();

        var result = await handler.HandleAsync(
            new AuthorizePaymentCommand
            {
                PaymentId = Guid.CreateVersion7(),
                CorrelationId = Guid.CreateVersion7(),
                BuyerId = Guid.CreateVersion7(),
                OrderId = Guid.CreateVersion7(),
                Amount = 0m,
                Currency = "USD",
                PaymentMethodId = "tok_visa_4242",
            },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            await _repository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
            await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<PaymentTransaction>(), Arg.Any<CancellationToken>());
        }
    }

    private static PaymentTransaction BuildAuthorizedAggregate()
    {
        var amount = Money.Create(100m, "USD").Value;
        var tx = PaymentTransaction.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            amount,
            "tok_visa_4242",
            Now).Value;
        _ = tx.PopDomainEvents();
        tx.Authorize("stub-precooked", new GatewayResponseCode("ok", "Approved"), Now);
        _ = tx.PopDomainEvents();
        return tx;
    }

    public void Dispose() => _provider.Dispose();
}
