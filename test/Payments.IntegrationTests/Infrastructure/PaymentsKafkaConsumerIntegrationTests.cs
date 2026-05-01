using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payments.Domain.Transactions.ValueObjects;
using Payments.Infrastructure.Messaging.Kafka.PaymentCommands;
using Payments.Infrastructure.Persistence.Database;
using Payments.IntegrationTests.Common;
using AvroAuthorizePaymentCommand = Payments.Transactions.AuthorizePaymentCommand;
using AvroCapturePaymentCommand = Payments.Transactions.CapturePaymentCommand;
using AvroPaymentAuthorizationFailedEvent = Payments.Transactions.PaymentAuthorizationFailedEvent;
using AvroPaymentAuthorizedEvent = Payments.Transactions.PaymentAuthorizedEvent;
using AvroPaymentCapturedEvent = Payments.Transactions.PaymentCapturedEvent;
using AvroPaymentRefundedEvent = Payments.Transactions.PaymentRefundedEvent;
using AvroPaymentVoidedEvent = Payments.Transactions.PaymentVoidedEvent;
using AvroRequestRefundCommand = Payments.Transactions.RequestRefundCommand;
using AvroVoidPaymentCommand = Payments.Transactions.VoidPaymentCommand;

namespace Payments.IntegrationTests.Infrastructure;

/// <summary>
/// End-to-end integration tests for the M5 Kafka consumer wiring. Each scenario produces an
/// Avro saga-command, invokes the corresponding consumer handler directly via a
/// <see cref="FakeKafkaMessageContext"/> stub, and verifies the persisted aggregate state in
/// Postgres + the captured outbox emissions in <c>FakeOutboxWriter</c>. The full Avro byte-level
/// roundtrip against a real Schema Registry is exercised by the docker-compose smoke test (M9);
/// the purpose at M5 is to prove the Infrastructure layer composes correctly.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class PaymentsKafkaConsumerIntegrationTests
{
    private readonly IntegrationTestFixture _fixture;

    public PaymentsKafkaConsumerIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        // Each test starts with a clean outbox capture; aggregate rows survive between tests
        // by design — every scenario uses fresh GUIDs so collisions don't occur.
        _fixture.GetFakeOutbox().Clear();
    }

    [Fact]
    public async Task Authorize_HappyPath_PersistsAggregate_AndOutboxesPaymentAuthorizedEvent()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var avro = NewAvroAuthorize(correlationId, orderId, amount: 100.00m);

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<AuthorizePaymentCommandKafkaHandler>();

        await handler.Handle(
            FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
            avro);

        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var aggregate = await dbContext.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.CorrelationId == correlationId, TestContext.Current.CancellationToken);

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
            msg.IntegrationEvent.CorrelationId.Should().Be(correlationId);
        }
    }

    [Fact]
    public async Task Authorize_DeclineRule_TransitionsAggregateToFailed_AndOutboxesAuthorizationFailedEventOnly()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        // Stub gateway rule: amount ending .99 declines (per M3 docs).
        var avro = NewAvroAuthorize(correlationId, orderId, amount: 9.99m);

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<AuthorizePaymentCommandKafkaHandler>();

        await handler.Handle(
            FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
            avro);

        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var aggregate = await dbContext.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.CorrelationId == correlationId, TestContext.Current.CancellationToken);

        var outbox = _fixture.GetFakeOutbox();

        using (new AssertionScope())
        {
            aggregate.Should().NotBeNull();
            aggregate!.Status.Should().Be(PaymentStatus.Failed);
            aggregate.FailureInfo.Should().NotBeNull();

            // PaymentAuthorizationFailedEvent is emitted by Payments via the outbox; the
            // terminal PaymentFailedEvent is the saga's responsibility (Path B from M4) and
            // is therefore NOT expected on the Payments-side outbox.
            outbox.HasMessage<AvroPaymentAuthorizationFailedEvent>().Should().BeTrue();
            outbox.HasMessage<AvroPaymentAuthorizedEvent>().Should().BeFalse();
        }
    }

    [Fact]
    public async Task Capture_AfterAuthorize_TransitionsToCompleted_AndOutboxesCapturedEventOnly()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();

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
                CorrelationId = correlationId,
                UserId = Guid.CreateVersion7(),
                AuthorizationId = "stub-auth-ignored",
                Amount = new Avro.AvroDecimal(100m),
                RequestedAtUtc = _fixture.FakeTime.GetUtcNow().UtcDateTime,
            });

        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var aggregate = await dbContext.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.CorrelationId == correlationId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            aggregate.Should().NotBeNull();
            // Aggregate auto-advances Captured -> Completed (v1 single-step flow per
            // payments.md § 4). PaymentCapturedEvent is emitted by Payments; the terminal
            // PaymentCompletedEvent is the saga's responsibility (Path B) and is NOT expected
            // on the Payments-side outbox.
            aggregate!.Status.Should().Be(PaymentStatus.Completed);
            outbox.HasMessage<AvroPaymentCapturedEvent>().Should().BeTrue();
        }
    }

    [Fact]
    public async Task Void_AfterAuthorize_TransitionsToVoided_AndOutboxesPaymentVoidedEvent()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();

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
                CorrelationId = correlationId,
                UserId = Guid.CreateVersion7(),
                AuthorizationId = "stub-auth-ignored",
                Reason = "saga compensation",
                RequestedAtUtc = _fixture.FakeTime.GetUtcNow().UtcDateTime,
            });

        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var aggregate = await dbContext.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.CorrelationId == correlationId, TestContext.Current.CancellationToken);
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
        var orderId = Guid.CreateVersion7();
        // For Authorize/Capture/Void the M5 mapper derives PaymentId from CorrelationId
        // (one-payment-per-saga). For RequestRefund the mapper uses
        // `avro.PaymentTransactionId` directly — the saga targets the existing aggregate
        // explicitly. The persisted aggregate's `Id` was set by the Authorize step to
        // `correlationId`, so the saga must echo that value here.
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
                CorrelationId = correlationId,
                UserId = Guid.CreateVersion7(),
                AuthorizationId = "stub-auth-ignored",
                Amount = new Avro.AvroDecimal(75m),
                RequestedAtUtc = _fixture.FakeTime.GetUtcNow().UtcDateTime,
            });

        _fixture.GetFakeOutbox().Clear();

        await refundHandler.Handle(
            FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
            new AvroRequestRefundCommand
            {
                CorrelationId = correlationId,
                UserId = Guid.CreateVersion7(),
                PaymentTransactionId = paymentId,
                Reason = "buyer cancelled after delivery",
                RequestedAtUtc = _fixture.FakeTime.GetUtcNow().UtcDateTime,
            });

        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var aggregate = await dbContext.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.CorrelationId == correlationId, TestContext.Current.CancellationToken);
        var outbox = _fixture.GetFakeOutbox();

        using (new AssertionScope())
        {
            aggregate.Should().NotBeNull();
            aggregate!.Status.Should().Be(PaymentStatus.Refunded);
            outbox.HasMessage<AvroPaymentRefundedEvent>().Should().BeTrue();
        }
    }

    private AvroAuthorizePaymentCommand NewAvroAuthorize(Guid correlationId, Guid orderId, decimal amount) =>
        new()
        {
            CorrelationId = correlationId,
            OrderId = orderId,
            UserId = Guid.CreateVersion7(),
            PaymentMethodId = Guid.CreateVersion7(),
            Amount = new Avro.AvroDecimal(amount),
            Currency = "USD",
            IdempotencyKey = $"key-{correlationId}",
            RequestedAtUtc = _fixture.FakeTime.GetUtcNow().UtcDateTime,
        };
}
