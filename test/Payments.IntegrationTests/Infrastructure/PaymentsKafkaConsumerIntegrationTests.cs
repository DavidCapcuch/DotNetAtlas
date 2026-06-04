using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payments.Domain.Transactions;
using Payments.Domain.Transactions.ValueObjects;
using Payments.Infrastructure.Messaging.Kafka.PaymentCommands;
using Payments.Infrastructure.Persistence.Database;
using Payments.IntegrationTests.Common;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;
using Platform.Test.Framework.Kafka;
using AvroAuthorizePaymentCommand = Payments.Transactions.AuthorizePaymentCommand;
using AvroCapturePaymentCommand = Payments.Transactions.CapturePaymentCommand;
using AvroPaymentAuthorizationFailedEvent = Payments.Transactions.PaymentAuthorizationFailedEvent;
using AvroPaymentAuthorizedEvent = Payments.Transactions.PaymentAuthorizedEvent;
using AvroPaymentCapturedEvent = Payments.Transactions.PaymentCapturedEvent;
using AvroPaymentCompletedEvent = Payments.Transactions.PaymentCompletedEvent;
using AvroPaymentFailedEvent = Payments.Transactions.PaymentFailedEvent;
using AvroPaymentRefundedEvent = Payments.Transactions.PaymentRefundedEvent;
using AvroPaymentVoidedEvent = Payments.Transactions.PaymentVoidedEvent;
using AvroRequestRefundCommand = Payments.Transactions.RequestRefundCommand;
using AvroVoidPaymentCommand = Payments.Transactions.VoidPaymentCommand;

namespace Payments.IntegrationTests.Infrastructure;

/// <summary>
/// End-to-end integration tests for the Kafka consumer wiring. Each scenario produces an
/// Avro saga-command, invokes the corresponding consumer handler directly via a
/// <see cref="FakeKafkaMessageContext"/> stub, and verifies the persisted aggregate state in
/// Postgres + the captured outbox emissions in <c>FakeOutboxWriter</c>. The full Avro byte-level
/// roundtrip against a real Schema Registry is exercised by the docker-compose smoke test;
/// the purpose is to prove the Infrastructure layer composes correctly.
/// </summary>
/// <remarks>
/// ADR-0029 keys the saga on <c>OrderId</c> with <c>CorrelationId == OrderId</c>, so every
/// scenario sets <c>orderId = correlationId</c>. This matters for Capture / Void, which carry no
/// PaymentTransactionId and resolve the aggregate via the unique <c>order_id</c> index (ADR-0030
/// retired the correlation-id lookup); the seeded OrderId must equal the saga key on the wire.
/// </remarks>
[Collection<IntegrationTestCollection>]
public sealed class PaymentsKafkaConsumerIntegrationTests
{
    private readonly IntegrationTestFixture _fixture;

    public PaymentsKafkaConsumerIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        // Each test starts with a clean outbox capture and zeroed gateway counters; aggregate
        // rows survive between tests by design — every scenario uses fresh GUIDs so collisions
        // don't occur.
        _fixture.GetFakeOutbox().Clear();
        _fixture.GetGateway().Reset();
    }

    [Fact]
    public async Task Authorize_HappyPath_PersistsAggregate_AndOutboxesPaymentAuthorizedEvent()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = correlationId; // ADR-0029: CorrelationId == OrderId (see class remarks).
        var avro = NewAvroAuthorize(correlationId, orderId, amount: 100.00m);

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<AuthorizePaymentCommandKafkaHandler>();

        await handler.Handle(
            FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
            avro);

        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var aggregate = await dbContext.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.OrderId == orderId, TestContext.Current.CancellationToken);

        var outbox = _fixture.GetFakeOutbox();

        using (new AssertionScope())
        {
            aggregate.Should().NotBeNull();
            aggregate!.Status.Should().Be(PaymentStatus.Authorized);
            aggregate.OrderId.Should().Be(orderId);
            aggregate.GatewayTransactionId.Should().NotBeNullOrEmpty();

            outbox.HasMessage<AvroPaymentAuthorizedEvent>().Should().BeTrue();
            var msg = outbox.GetMessages<AvroPaymentAuthorizedEvent>().Single();
            msg.TopicName.Should().Be("payments.transactions");
            msg.KafkaKey.Should().Be(correlationId.ToString());
        }
    }

    [Fact]
    public async Task Authorize_DeclineRule_TransitionsAggregateToFailed_AndOutboxesAuthorizationFailedAndTerminalFailedEvents()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = correlationId; // ADR-0029: CorrelationId == OrderId (see class remarks).
        // Stub gateway rule: amount ending .99 declines (per M3 docs).
        var avro = NewAvroAuthorize(correlationId, orderId, amount: 9.99m);

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<AuthorizePaymentCommandKafkaHandler>();

        await handler.Handle(
            FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
            avro);

        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var aggregate = await dbContext.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.OrderId == orderId, TestContext.Current.CancellationToken);

        var outbox = _fixture.GetFakeOutbox();

        using (new AssertionScope())
        {
            aggregate.Should().NotBeNull();
            aggregate!.Status.Should().Be(PaymentStatus.Failed);
            aggregate.FailureInfo.Should().NotBeNull();

            // ADR-0026: Payments owns ALL its lifecycle events, including the terminal
            // PaymentFailedEvent — co-raised with PaymentAuthorizationFailedEvent on a decline so
            // the Checkout saga can fast-fail. PaymentProcessingSaga no longer publishes it.
            outbox.HasMessage<AvroPaymentAuthorizationFailedEvent>().Should().BeTrue();
            outbox.HasMessage<AvroPaymentFailedEvent>().Should().BeTrue();
            outbox.HasMessage<AvroPaymentAuthorizedEvent>().Should().BeFalse();

            var failed = outbox.GetMessages<AvroPaymentFailedEvent>().Single();
            failed.TopicName.Should().Be("payments.transactions");
            failed.KafkaKey.Should().Be(correlationId.ToString());
        }
    }

    [Fact]
    public async Task Capture_AfterAuthorize_TransitionsToCompleted_AndOutboxesCapturedAndTerminalCompletedEvents()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = correlationId; // ADR-0029: CorrelationId == OrderId (see class remarks).

        using var scope = _fixture.CreateScope();
        var authorizeHandler = scope.ServiceProvider.GetRequiredService<AuthorizePaymentCommandKafkaHandler>();
        var captureHandler = scope.ServiceProvider.GetRequiredService<CapturePaymentCommandKafkaHandler>();

        await authorizeHandler.Handle(
            FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
            NewAvroAuthorize(correlationId, orderId, amount: 100m));

        var outbox = _fixture.GetFakeOutbox();
        outbox.Clear();

        await captureHandler.Handle(
            FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
            new AvroCapturePaymentCommand
            {
                OrderId = orderId,
                UserId = Guid.CreateVersion7(),
                AuthorizationId = StoredGatewayTransactionId(correlationId),
                Amount = new Avro.AvroDecimal(100m),
                RequestedAtUtc = DateTime.UtcNow,
            });

        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var aggregate = await dbContext.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.OrderId == orderId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            aggregate.Should().NotBeNull();
            // Aggregate auto-advances Captured -> Completed (v1 single-step flow per
            // payments.md § 4). ADR-0026: Payments owns ALL its lifecycle events — both
            // PaymentCapturedEvent and the terminal PaymentCompletedEvent are emitted by the
            // Payments-side outbox; PaymentProcessingSaga no longer publishes the terminal.
            aggregate!.Status.Should().Be(PaymentStatus.Completed);
            outbox.HasMessage<AvroPaymentCapturedEvent>().Should().BeTrue();
            outbox.HasMessage<AvroPaymentCompletedEvent>().Should().BeTrue();

            var completed = outbox.GetMessages<AvroPaymentCompletedEvent>().Single();
            completed.TopicName.Should().Be("payments.transactions");
            completed.KafkaKey.Should().Be(correlationId.ToString());
            completed.IntegrationEvent.PaymentTransactionId.Should().Be(correlationId);
        }
    }

    [Fact]
    public async Task Void_AfterAuthorize_TransitionsToVoided_AndOutboxesPaymentVoidedEvent()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = correlationId; // ADR-0029: CorrelationId == OrderId (see class remarks).

        using var scope = _fixture.CreateScope();
        var authorizeHandler = scope.ServiceProvider.GetRequiredService<AuthorizePaymentCommandKafkaHandler>();
        var voidHandler = scope.ServiceProvider.GetRequiredService<VoidPaymentCommandKafkaHandler>();

        await authorizeHandler.Handle(
            FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
            NewAvroAuthorize(correlationId, orderId, amount: 50m));

        _fixture.GetFakeOutbox().Clear();

        await voidHandler.Handle(
            FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
            new AvroVoidPaymentCommand
            {
                OrderId = orderId,
                UserId = Guid.CreateVersion7(),
                AuthorizationId = StoredGatewayTransactionId(correlationId),
                Reason = "saga compensation",
                RequestedAtUtc = DateTime.UtcNow,
            });

        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var aggregate = await dbContext.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.OrderId == orderId, TestContext.Current.CancellationToken);
        var outbox = _fixture.GetFakeOutbox();

        using (new AssertionScope())
        {
            aggregate.Should().NotBeNull();
            aggregate!.Status.Should().Be(PaymentStatus.Voided);
            outbox.HasMessage<AvroPaymentVoidedEvent>().Should().BeTrue();
        }
    }

    [Fact]
    public async Task Refund_AfterCapture_TransitionsToRefunded_AndOutboxesPaymentRefundedEvent()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = correlationId; // ADR-0029: CorrelationId == OrderId (see class remarks).
        // Capture / Void resolve the aggregate by OrderId (ADR-0030); RequestRefund resolves it by
        // primary key from `avro.PaymentTransactionId` — the saga targets the existing aggregate
        // explicitly. The persisted aggregate's `Id` was set by the Authorize step to
        // `correlationId` (NewAvroAuthorize collapses PaymentTransactionId onto it), so the refund
        // must echo that value here.
        var paymentId = correlationId;

        using var scope = _fixture.CreateScope();
        var authorizeHandler = scope.ServiceProvider.GetRequiredService<AuthorizePaymentCommandKafkaHandler>();
        var captureHandler = scope.ServiceProvider.GetRequiredService<CapturePaymentCommandKafkaHandler>();
        var refundHandler = scope.ServiceProvider.GetRequiredService<RequestRefundCommandKafkaHandler>();

        await authorizeHandler.Handle(
            FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
            NewAvroAuthorize(correlationId, orderId, amount: 75m));

        await captureHandler.Handle(
            FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
            new AvroCapturePaymentCommand
            {
                OrderId = orderId,
                UserId = Guid.CreateVersion7(),
                AuthorizationId = StoredGatewayTransactionId(correlationId),
                Amount = new Avro.AvroDecimal(75m),
                RequestedAtUtc = DateTime.UtcNow,
            });

        _fixture.GetFakeOutbox().Clear();

        await refundHandler.Handle(
            FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
            new AvroRequestRefundCommand
            {
                UserId = Guid.CreateVersion7(),
                PaymentTransactionId = paymentId,
                Reason = "buyer cancelled after delivery",
                RequestedAtUtc = DateTime.UtcNow,
            });

        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var aggregate = await dbContext.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.OrderId == orderId, TestContext.Current.CancellationToken);
        var outbox = _fixture.GetFakeOutbox();

        using (new AssertionScope())
        {
            aggregate.Should().NotBeNull();
            aggregate!.Status.Should().Be(PaymentStatus.Refunded);
            outbox.HasMessage<AvroPaymentRefundedEvent>().Should().BeTrue();
        }
    }

    [Fact]
    public async Task Capture_WithoutPriorAuthorize_AggregateInRequested_ThrowsDataIntegrityException()
    {
        // Example 1.2 in docs/bc-design/example-mapping/payments.md: skipping Authorize is a
        // saga-ordering bug. Seed an aggregate in Requested status (no GatewayTransactionId)
        // and drive Capture directly — handler's FSM CanTransitionTo pre-check (H-Cond-2) fires
        // BEFORE any gateway call and throws `Payments.InvalidStatusTransition`. The legacy
        // `Payments.MissingGatewayTransactionId` null-guard was removed in #250 — the aggregate's
        // FSM is the single source of truth and the handler-level guard was unreachable.
        var correlationId = Guid.CreateVersion7();
        var orderId = correlationId; // ADR-0029: CorrelationId == OrderId (see class remarks).
        var paymentId = correlationId;

        using var scope = _fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        var amount = Money.Create(100m, "USD").Value;
        var tx = PaymentTransaction.Create(
            paymentId,
            buyerId: Guid.CreateVersion7(),
            orderId,
            amount,
            paymentMethodId: "tok_visa_4242").Value;

        // ADR-0023 follow-up: PaymentTransaction.Create raises no domain events, so PopDomainEvents()
        // here returns an empty collection. The call is retained defensively to make the "no events
        // leak into the seed save" invariant explicit for future readers.
        _ = tx.PopDomainEvents();
        dbContext.Transactions.Add(tx);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _fixture.GetFakeOutbox().Clear();
        _fixture.GetGateway().Reset();

        var captureHandler = scope.ServiceProvider.GetRequiredService<CapturePaymentCommandKafkaHandler>();

        var avroCapture = new AvroCapturePaymentCommand
        {
            OrderId = orderId,
            UserId = Guid.CreateVersion7(),
            AuthorizationId = "stub-auth-ignored",
            Amount = new Avro.AvroDecimal(100m),
            RequestedAtUtc = DateTime.UtcNow,
        };

        var thrown = await Assert.ThrowsAsync<DataIntegrityException>(async () =>
            await captureHandler.Handle(
                FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
                avroCapture));

        var aggregateAfter = await dbContext.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.OrderId == orderId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            thrown.ErrorCode.Should().Be("Payments.InvalidStatusTransition");
            aggregateAfter.Should().NotBeNull();
            aggregateAfter!.Status.Should().Be(PaymentStatus.Requested);
            aggregateAfter.GatewayTransactionId.Should().BeNull();
            _fixture.GetGateway().CaptureCount.Should().Be(0);
            _fixture.GetFakeOutbox().GetMessages<AvroPaymentCapturedEvent>().Should().BeEmpty();
        }
    }

    [Fact]
    public async Task AuthorizeRetry_AggregateInFailedStatus_IsIdempotent_GatewayNotCalled_NoNewOutbox()
    {
        // Example 2.2 in docs/bc-design/example-mapping/payments.md: a declined aggregate is
        // terminal; replaying the same AuthorizePaymentCommand must short-circuit before the
        // gateway is touched and emit no new domain events / outbox rows.
        var correlationId = Guid.CreateVersion7();
        var orderId = correlationId; // ADR-0029: CorrelationId == OrderId (see class remarks).
        var avro = NewAvroAuthorize(correlationId, orderId, amount: 9.99m);

        using var scope = _fixture.CreateScope();
        var authorizeHandler = scope.ServiceProvider.GetRequiredService<AuthorizePaymentCommandKafkaHandler>();

        // Phase 1: drive the decline to land the aggregate in Failed and emit
        // PaymentAuthorizationFailedEvent on the outbox.
        await authorizeHandler.Handle(
            FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
            avro);

        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var afterFirst = await dbContext.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.OrderId == orderId, TestContext.Current.CancellationToken);

        afterFirst.Should().NotBeNull();
        afterFirst!.Status.Should().Be(PaymentStatus.Failed);
        _fixture.GetFakeOutbox().HasMessage<AvroPaymentAuthorizationFailedEvent>().Should().BeTrue();

        // Phase 2: reset spies and replay the *same* command to assert idempotency.
        _fixture.GetFakeOutbox().Clear();
        _fixture.GetGateway().Reset();

        await authorizeHandler.Handle(
            FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
            avro);

        var afterRetry = await dbContext.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.OrderId == orderId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            afterRetry.Should().NotBeNull();
            afterRetry!.Status.Should().Be(PaymentStatus.Failed);
            afterRetry.FailureInfo.Should().NotBeNull();
            afterRetry.FailureInfo!.GatewayCode.Should().Be(afterFirst.FailureInfo!.GatewayCode);

            _fixture.GetGateway().AuthorizeCount.Should().Be(0);
            // Spec literal "no new outbox rows" — type-blind so a future regression that
            // emits some unexpected event type still fails the test (Opus pre-commit reviewer
            // recommendation).
            _fixture.GetFakeOutbox().CapturedMessages.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Void_AfterCapture_AggregateInCompleted_ThrowsDataIntegrityException_NoGatewayCall_NoStateChange()
    {
        // Example 3.3 in docs/bc-design/example-mapping/payments.md: void post-capture is a
        // saga bug-class. The aggregate FSM rejects the Completed → Voided transition with a
        // DataIntegrityException; aggregate state, emitted events, AND the gateway stay clean
        // — the handler's CanTransitionTo pre-check (H-Cond-2) fires before any gateway call,
        // so a real PSP never sees the bogus Void.
        var correlationId = Guid.CreateVersion7();
        var orderId = correlationId; // ADR-0029: CorrelationId == OrderId (see class remarks).

        using var scope = _fixture.CreateScope();
        var authorizeHandler = scope.ServiceProvider.GetRequiredService<AuthorizePaymentCommandKafkaHandler>();
        var captureHandler = scope.ServiceProvider.GetRequiredService<CapturePaymentCommandKafkaHandler>();
        var voidHandler = scope.ServiceProvider.GetRequiredService<VoidPaymentCommandKafkaHandler>();

        await authorizeHandler.Handle(
            FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
            NewAvroAuthorize(correlationId, orderId, amount: 50m));

        await captureHandler.Handle(
            FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
            new AvroCapturePaymentCommand
            {
                OrderId = orderId,
                UserId = Guid.CreateVersion7(),
                AuthorizationId = StoredGatewayTransactionId(correlationId),
                Amount = new Avro.AvroDecimal(50m),
                RequestedAtUtc = DateTime.UtcNow,
            });

        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var afterCapture = await dbContext.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.OrderId == orderId, TestContext.Current.CancellationToken);
        afterCapture.Should().NotBeNull();
        afterCapture!.Status.Should().Be(PaymentStatus.Completed);

        _fixture.GetFakeOutbox().Clear();
        _fixture.GetGateway().Reset();

        var thrown = await Assert.ThrowsAsync<DataIntegrityException>(async () =>
            await voidHandler.Handle(
                FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
                new AvroVoidPaymentCommand
                {
                    OrderId = orderId,
                    UserId = Guid.CreateVersion7(),
                    AuthorizationId = StoredGatewayTransactionId(correlationId),
                    Reason = "saga ordering bug — should have refunded",
                    RequestedAtUtc = DateTime.UtcNow,
                }));

        var afterVoidAttempt = await dbContext.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.OrderId == orderId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            thrown.ErrorCode.Should().Be("Payments.InvalidStatusTransition");
            afterVoidAttempt.Should().NotBeNull();
            afterVoidAttempt!.Status.Should().Be(PaymentStatus.Completed);
            afterVoidAttempt.VoidedAtUtc.Should().BeNull();
            _fixture.GetFakeOutbox().GetMessages<AvroPaymentVoidedEvent>().Should().BeEmpty();
            // H-Cond-2: FSM pre-check fires before gateway, so the PSP is never touched.
            _fixture.GetGateway().VoidCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task Void_AuthorizationIdMismatch_ThrowsDataIntegrityException_NoGatewayCall()
    {
        // H-8: a wire AuthorizationId that disagrees with the stored GatewayTransactionId
        // (saga bug, stale-token replay) must throw before the gateway is touched. The
        // KafkaFlow retry middleware classifies DataIntegrityException as poison and routes
        // the message to the `payments.payment-commands` DLT for operator inspection.
        var correlationId = Guid.CreateVersion7();
        var orderId = correlationId; // ADR-0029: CorrelationId == OrderId (see class remarks).

        using var scope = _fixture.CreateScope();
        var authorizeHandler = scope.ServiceProvider.GetRequiredService<AuthorizePaymentCommandKafkaHandler>();
        var voidHandler = scope.ServiceProvider.GetRequiredService<VoidPaymentCommandKafkaHandler>();

        await authorizeHandler.Handle(
            FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
            NewAvroAuthorize(correlationId, orderId, amount: 50m));

        _fixture.GetFakeOutbox().Clear();
        _fixture.GetGateway().Reset();

        var thrown = await Assert.ThrowsAsync<DataIntegrityException>(async () =>
            await voidHandler.Handle(
                FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
                new AvroVoidPaymentCommand
                {
                    OrderId = orderId,
                    UserId = Guid.CreateVersion7(),
                    AuthorizationId = "wire-token-stale",
                    Reason = "saga compensation",
                    RequestedAtUtc = DateTime.UtcNow,
                }));

        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var aggregateAfter = await dbContext.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.OrderId == orderId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            thrown.ErrorCode.Should().Be("Payments.AuthorizationIdMismatch");
            aggregateAfter.Should().NotBeNull();
            aggregateAfter!.Status.Should().Be(PaymentStatus.Authorized);
            _fixture.GetGateway().VoidCount.Should().Be(0);
            _fixture.GetFakeOutbox().GetMessages<AvroPaymentVoidedEvent>().Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Authorize_PropagatesIdempotencyKey_FromWireToGateway()
    {
        // H-4: the saga-issued IdempotencyKey on the Avro wire command must flow through the
        // application command record and reach IPaymentGateway.AuthorizeAsync, where a v2 real
        // adapter will forward it as the gateway's Idempotency-Key header.
        var correlationId = Guid.CreateVersion7();
        var orderId = correlationId; // ADR-0029: CorrelationId == OrderId (see class remarks).
        var avro = NewAvroAuthorize(correlationId, orderId, amount: 50m);

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<AuthorizePaymentCommandKafkaHandler>();

        await handler.Handle(
            FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
            avro);

        _fixture.GetGateway().LastAuthorizeIdempotencyKey.Should().Be(avro.IdempotencyKey);
    }

    // Stub gateway derives gateway-transaction-id deterministically as $"stub-{tx.Id:N}";
    // tx.Id is set to correlationId in the M5 mapper, so the stored value is exactly this.
    // Saga-side wire commands must echo this value, otherwise the H-8 AuthorizationId validation
    // in the handlers rejects them as stale-token / saga-bug replays.
    private static string StoredGatewayTransactionId(Guid correlationId) => $"stub-{correlationId:N}";

    private AvroAuthorizePaymentCommand NewAvroAuthorize(Guid correlationId, Guid orderId, decimal amount) =>
        new()
        {
            // Cross-cutting wave1-followup #255: the production saga mints a fresh v7
            // PaymentTransactionId at initial state and the Payments mapper uses it as the
            // aggregate PK. For this integration-test helper we deliberately collapse the two
            // ids onto the same value so the existing test assertions that derive the stored
            // GatewayTransactionId via $"stub-{correlationId:N}" continue to match what
            // StubPaymentGateway produces from tx.Id. Tests focused on the PaymentTransactionId-
            // distinct-from-CorrelationId contract live in
            // SagaCommandMappersTests + PaymentProcessingSagaOrchestratorTests.
            PaymentTransactionId = correlationId,
            OrderId = orderId,
            UserId = Guid.CreateVersion7(),
            // Real-PSP-shaped token (Stripe-style 'pm_*' string) per C-2 closeout — the Avro
            // contract is now plain string, not logicalType:uuid.
            PaymentMethodId = $"pm_{Guid.CreateVersion7():N}",
            Amount = new Avro.AvroDecimal(amount),
            Currency = "USD",
            IdempotencyKey = $"key-{correlationId}",
            RequestedAtUtc = DateTime.UtcNow,
        };
}
