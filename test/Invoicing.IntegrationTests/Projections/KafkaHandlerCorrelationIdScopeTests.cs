using System.Text;
using Invoicing.Infrastructure.Messaging.Kafka.Projections;
using Invoicing.Infrastructure.Persistence.Database;
using Invoicing.IntegrationTests.Common;
using KafkaFlow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Platform.Messaging.Abstractions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using AvroOrderCancelledEvent = Ordering.Orders.OrderCancelledEvent;
using AvroOrderConfirmedEvent = Ordering.Orders.OrderConfirmedEvent;
using AvroPaymentCapturedEvent = Payments.Transactions.PaymentCapturedEvent;
using AvroPaymentRefundedEvent = Payments.Transactions.PaymentRefundedEvent;

namespace Invoicing.IntegrationTests.Projections;

/// <summary>
/// ADR-0008 pin: the Kafka header <c>correlation-id</c> is authoritative for log /
/// trace correlation. The platform's <c>ConsumerCorrelationIdMiddleware</c> pushes
/// the header value into Serilog <c>LogContext</c> under the property name
/// <c>CorrelationId</c> BEFORE each typed handler runs. If a handler then opens
/// its own <c>_logger.BeginScope(new Dictionary&lt;…&gt; { ["CorrelationId"] = payload })</c>
/// the inner scope shadows the middleware-pushed value — making the payload's
/// <c>CorrelationId</c> field (which is NOT authoritative per ADR-0008) win for
/// log enrichment. This test pins each projection handler to the contract: open
/// scopes are allowed, but they must not include <c>CorrelationId</c> as a key.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class KafkaHandlerCorrelationIdScopeTests
{
    private readonly IntegrationTestFixture _fixture;

    public KafkaHandlerCorrelationIdScopeTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OrderConfirmedHandler_DoesNotShadow_LogContextCorrelationId()
    {
        var ct = TestContext.Current.CancellationToken;
        var recorder = new ScopeRecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(recorder));

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        var handler = new OrderConfirmedInvoiceProjectionKafkaHandler(
            db,
            M7CommandHandlerStubs.NoOpIssueInvoiceHandler(),
            new FakeTimeProvider(new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero)),
            loggerFactory.CreateLogger<OrderConfirmedInvoiceProjectionKafkaHandler>());

        await handler.Handle(BuildContext(ct), BuildOrderConfirmedEvent());

        recorder.AllScopeKeys.Should().NotContain(
            "CorrelationId",
            because: "handler must not shadow the middleware-pushed LogContext.CorrelationId (header = SSOT per ADR-0008)");
    }

    [Fact]
    public async Task OrderCancelledHandler_DoesNotShadow_LogContextCorrelationId()
    {
        var ct = TestContext.Current.CancellationToken;
        var recorder = new ScopeRecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(recorder));

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        var handler = new OrderCancelledCreditNoteProjectionKafkaHandler(
            db,
            M7CommandHandlerStubs.NoOpIssueCreditNoteHandler(),
            new FakeTimeProvider(new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero)),
            loggerFactory.CreateLogger<OrderCancelledCreditNoteProjectionKafkaHandler>());

        await handler.Handle(BuildContext(ct), BuildOrderCancelledEvent());

        recorder.AllScopeKeys.Should().NotContain("CorrelationId");
    }

    [Fact]
    public async Task PaymentCapturedHandler_DoesNotShadow_LogContextCorrelationId()
    {
        var ct = TestContext.Current.CancellationToken;
        var recorder = new ScopeRecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(recorder));

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        var handler = new PaymentCapturedInvoiceProjectionKafkaHandler(
            db,
            M7CommandHandlerStubs.NoOpIssueInvoiceHandler(),
            new FakeTimeProvider(new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero)),
            loggerFactory.CreateLogger<PaymentCapturedInvoiceProjectionKafkaHandler>());

        await handler.Handle(BuildContext(ct), BuildPaymentCapturedEvent());

        recorder.AllScopeKeys.Should().NotContain("CorrelationId");
    }

    [Fact]
    public async Task PaymentRefundedHandler_DoesNotShadow_LogContextCorrelationId()
    {
        var ct = TestContext.Current.CancellationToken;
        var recorder = new ScopeRecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(recorder));

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        var handler = new PaymentRefundedCreditNoteProjectionKafkaHandler(
            db,
            M7CommandHandlerStubs.NoOpIssueCreditNoteHandler(),
            new FakeTimeProvider(new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero)),
            loggerFactory.CreateLogger<PaymentRefundedCreditNoteProjectionKafkaHandler>());

        await handler.Handle(BuildContext(ct), BuildPaymentRefundedEvent());

        recorder.AllScopeKeys.Should().NotContain("CorrelationId");
    }

    private static IMessageContext BuildContext(CancellationToken ct)
    {
        var context = Substitute.For<IMessageContext>();
        // ADR-0008 — projection handlers now read CorrelationId from this header; the test
        // pin is about LogContext scope shadowing, but a real header must be present or the
        // handler short-circuits with InvalidOperationException before reaching the scope.
        context.Headers.Returns(new MessageHeaders
        {
            {
                MessageHeaderKeys.CorrelationId,
                Encoding.UTF8.GetBytes(Guid.CreateVersion7().ToString())
            },
        });
        var consumerContext = Substitute.For<IConsumerContext>();
        consumerContext.WorkerStopped.Returns(ct);
        context.ConsumerContext.Returns(consumerContext);
        return context;
    }

    private static AvroOrderConfirmedEvent BuildOrderConfirmedEvent()
    {
        return new AvroOrderConfirmedEvent
        {
            OrderId = Guid.CreateVersion7(),
            CorrelationId = Guid.CreateVersion7(),
            BuyerId = Guid.CreateVersion7(),
            ConfirmedAtUtc = DateTime.UtcNow,
            Items = [],
            TotalAmount = 0m.ToAvroDecimal(4),
            Currency = "EUR",
            BillingAddress = null,
        };
    }

    private static AvroOrderCancelledEvent BuildOrderCancelledEvent()
    {
        return new AvroOrderCancelledEvent
        {
            OrderId = Guid.CreateVersion7(),
            CorrelationId = Guid.CreateVersion7(),
            BuyerId = Guid.CreateVersion7(),
            AtStatus = Ordering.Orders.OrderStatusAtTransition.Confirmed,
            Reason = "test",
            CancelledAtUtc = DateTime.UtcNow,
            Items = [],
            TotalAmount = 0m.ToAvroDecimal(4),
            Currency = "EUR",
            BillingAddress = null,
        };
    }

    private static AvroPaymentCapturedEvent BuildPaymentCapturedEvent()
    {
        return new AvroPaymentCapturedEvent
        {
            CorrelationId = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            PaymentTransactionId = Guid.CreateVersion7(),
            AuthorizationId = "auth",
            Amount = new Avro.AvroDecimal(0m),
            Currency = "EUR",
            CapturedAtUtc = DateTime.UtcNow,
        };
    }

    private static AvroPaymentRefundedEvent BuildPaymentRefundedEvent()
    {
        return new AvroPaymentRefundedEvent
        {
            CorrelationId = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            PaymentTransactionId = Guid.CreateVersion7(),
            RefundTransactionId = Guid.CreateVersion7(),
            RefundedAmount = new Avro.AvroDecimal(0m),
            Currency = "EUR",
            RefundedAtUtc = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// In-memory <see cref="ILoggerProvider"/> that captures the <see cref="IDictionary{TKey,TValue}"/>
    /// state passed to <see cref="ILogger.BeginScope{TState}"/>, exposing the union of keys observed
    /// so the test can assert which property names a handler pushed into its local scope.
    /// </summary>
    private sealed class ScopeRecordingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _allKeys = [];
        private readonly object _gate = new();

        public IReadOnlyList<string> AllScopeKeys
        {
            get
            {
                lock (_gate)
                {
                    return [.. _allKeys];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

        public void Dispose()
        {
        }

        private void RecordKey(string key)
        {
            lock (_gate)
            {
                _allKeys.Add(key);
            }
        }

        private sealed class RecordingLogger : ILogger
        {
            private readonly ScopeRecordingLoggerProvider _owner;

            public RecordingLogger(ScopeRecordingLoggerProvider owner)
            {
                _owner = owner;
            }

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
            {
                if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    foreach (var pair in pairs)
                    {
                        _owner.RecordKey(pair.Key);
                    }
                }

                return NullDisposable.Instance;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
            }

            private sealed class NullDisposable : IDisposable
            {
                public static readonly NullDisposable Instance = new();

                public void Dispose()
                {
                }
            }
        }
    }
}
