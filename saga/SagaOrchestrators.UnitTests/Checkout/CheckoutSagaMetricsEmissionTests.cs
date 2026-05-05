using System.Diagnostics.Metrics;
using System.Text.Json;
using Checkout.Sagas;
using Inventory.Reservations;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Platform.Test.Framework.Kafka;
using SagaOrchestrators.Checkout.CheckoutSaga;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;
using SagaOrchestrators.Checkout.CheckoutSaga.Observability;
using SagaOrchestrators.Checkout.CheckoutSaga.Schedules;
using SagaOrchestrators.Common.Observability;
using SagaOrchestrators.Common.Observability.Tracing;

namespace SagaOrchestrators.UnitTests.Checkout;

/// <summary>
/// M7 observability-emit assertions per <c>docs/bc-design/checkout-saga.md § 11.2</c> and
/// the <c>&lt;dod&gt;</c> requirement "CompensationStuck counter increments on
/// CompensationTimeout". Drives the saga to the three terminal compensation outcomes
/// (<see cref="CheckoutSagaOrchestrator.CompensationStuck"/> from each compensation
/// branch + <see cref="CheckoutSagaOrchestrator.Compensated"/>) and asserts the
/// corresponding instruments fire on the static <see cref="CheckoutSagaMetrics"/> meter
/// via <see cref="MeterListener"/>.
/// </summary>
/// <remarks>
/// <para>
/// Pattern mirrors <c>test/Inventory.IntegrationTests/Persistence/EventStoreRepositoryRehydrationMetricsTests</c>
/// (the canonical <see cref="MeterListener"/> usage in this repo): per-test scoped
/// listener filtered by <see cref="Meter.Name"/> = <see cref="ApplicationInfo.AppName"/>,
/// instrument name, and a pinned tag copy (the runtime reuses the
/// <see cref="ReadOnlySpan{T}"/> buffer between callbacks).
/// </para>
/// <para>
/// Helpers are kept local to this file (rather than shared with the existing
/// <c>CheckoutSagaOrchestratorTests</c>) so M7's deliverables stay cohesive in the
/// <c>Checkout/</c> folder and we don't regress the M3 — M6 test suite by
/// refactoring it.
/// </para>
/// </remarks>
[Collection(nameof(CheckoutMeterSerialCollection))]
public sealed class CheckoutSagaMetricsEmissionTests : IAsyncLifetime
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private readonly FakeTimeProvider _fakeTimeProvider = new();
    private readonly FakeOutboxWriter _fakeOutboxWriter = new();
    private ServiceProvider _provider = null!;
    private ITestHarness _testHarness = null!;
    private ISagaStateMachineTestHarness<CheckoutSagaOrchestrator, CheckoutSagaState> _sagaHarness = null!;

    public async ValueTask InitializeAsync()
    {
        var sagaOptions = SagaTestFixture.CreateSagaOptions();
        var topicsOptions = SagaTestFixture.CreateSagaTopicsOptions();
        var testDbName = $"SagaTest_{Guid.CreateVersion7()}";

        _provider = new ServiceCollection()
            .AddSingleton(Substitute.For<ILogger<CheckoutSagaOrchestrator>>())
            .AddSingleton(sagaOptions)
            .AddSingleton(topicsOptions)
            .AddSingleton<TimeProvider>(_fakeTimeProvider)
            .AddSagaOutboxTestServices(testDbName, _fakeOutboxWriter)
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddSagaStateMachine<CheckoutSagaOrchestrator, CheckoutSagaState>()
                    .InMemoryRepository();
            })
            .BuildServiceProvider(true);

        _testHarness = _provider.GetRequiredService<ITestHarness>();
        _sagaHarness = _testHarness.GetSagaStateMachineHarness<CheckoutSagaOrchestrator, CheckoutSagaState>();
        await _testHarness.Start();
    }

    public async ValueTask DisposeAsync()
    {
        await _testHarness.Stop();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task CompensatingStockReservationsCompensationTimeout_EmitsStuckAndCompensationTimeoutCounters()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();

        await ReachCompensatingStockReservations(correlationId, orderId, product1);

        var stuck = new List<long>();
        var stuckTags = new List<KeyValuePair<string, object?>[]>();
        var compensationTimeout = new List<long>();
        var compensationTimeoutTags = new List<KeyValuePair<string, object?>[]>();

        using var listener = BuildLongCounterListener(
            ("saga.checkout.stuck", stuck, stuckTags),
            ("saga.checkout.compensation_timeout", compensationTimeout, compensationTimeoutTags));
        listener.Start();

        await _testHarness.Bus.Publish(new CompensationTimeoutExpired { CorrelationId = correlationId });
        (await _sagaHarness.Consumed.Any<CompensationTimeoutExpired>()).Should().BeTrue();
        (await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null)
            .Should().BeTrue("CompensationStuck is abnormal-terminal");

        using (new AssertionScope())
        {
            stuck.Should().ContainSingle().Which.Should().Be(1, "saga.checkout.stuck increments by 1 on CompensationStuck");
            compensationTimeout.Should().ContainSingle().Which.Should().Be(1,
                "saga.checkout.compensation_timeout increments by 1 on CompensationTimeout fire");
            stuckTags.Should().ContainSingle().Which.Should().Contain(kv =>
                kv.Key == CheckoutSagaActivityTags.LastState
                && (string?)kv.Value == nameof(CheckoutSagaOrchestrator.CompensatingStockReservations));
            compensationTimeoutTags.Should().ContainSingle().Which.Should().Contain(kv =>
                kv.Key == CheckoutSagaActivityTags.LastState
                && (string?)kv.Value == nameof(CheckoutSagaOrchestrator.CompensatingStockReservations));
            stuckTags[0].Should().Contain(kv =>
                kv.Key == SagaActivityTags.ErrorCode && (string?)kv.Value == "COMPENSATION_TIMEOUT");
        }
    }

    [Fact]
    public async Task CompensatingPaymentCompensationTimeout_EmitsStuckAndCompensationTimeoutCounters()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();

        await ReachCompensatingPayment(correlationId, orderId, product1);

        var stuck = new List<long>();
        var stuckTags = new List<KeyValuePair<string, object?>[]>();
        var compensationTimeout = new List<long>();
        var compensationTimeoutTags = new List<KeyValuePair<string, object?>[]>();

        using var listener = BuildLongCounterListener(
            ("saga.checkout.stuck", stuck, stuckTags),
            ("saga.checkout.compensation_timeout", compensationTimeout, compensationTimeoutTags));
        listener.Start();

        await _testHarness.Bus.Publish(new CompensationTimeoutExpired { CorrelationId = correlationId });
        (await _sagaHarness.Consumed.Any<CompensationTimeoutExpired>()).Should().BeTrue();
        (await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null)
            .Should().BeTrue("CompensationStuck is abnormal-terminal");

        using (new AssertionScope())
        {
            stuck.Should().ContainSingle().Which.Should().Be(1);
            compensationTimeout.Should().ContainSingle().Which.Should().Be(1);
            stuckTags.Should().ContainSingle().Which.Should().Contain(kv =>
                kv.Key == CheckoutSagaActivityTags.LastState
                && (string?)kv.Value == nameof(CheckoutSagaOrchestrator.CompensatingPayment));
            compensationTimeoutTags.Should().ContainSingle().Which.Should().Contain(kv =>
                kv.Key == CheckoutSagaActivityTags.LastState
                && (string?)kv.Value == nameof(CheckoutSagaOrchestrator.CompensatingPayment));
        }
    }

    [Fact]
    public async Task CompensatedTerminal_EmitsCompensatedCounterAndCompensationDurationHistogram()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();

        await ReachCompensatingStockReservations(correlationId, orderId, product1);

        // Drive a non-zero compensation duration. The orchestrator stamps
        // CompensationStartedAtUtc inside DispatchStockReleaseAndCancelOrder (??=) at the
        // moment we entered CompensatingStockReservations; advancing the FakeTimeProvider
        // here makes the FinalizeCompensation timestamp strictly greater so the histogram
        // records a positive measurement.
        _fakeTimeProvider.Advance(TimeSpan.FromSeconds(5));

        var compensated = new List<long>();
        var compensatedTags = new List<KeyValuePair<string, object?>[]>();
        var compensationDurationMs = new List<double>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name != ApplicationInfo.AppName)
            {
                return;
            }

            if (instrument.Name is "saga.checkout.compensated" or "saga.checkout.compensation_duration_ms")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name == "saga.checkout.compensated")
            {
                compensated.Add(measurement);
                compensatedTags.Add(tags.ToArray());
            }
        });
        listener.SetMeasurementEventCallback<double>((instrument, measurement, _, _) =>
        {
            if (instrument.Name == "saga.checkout.compensation_duration_ms")
            {
                compensationDurationMs.Add(measurement);
            }
        });
        listener.Start();

        var stateInComp = _sagaHarness.Sagas.Contains(correlationId)!;
        var rid = GetReservationId(stateInComp, product1);

        await _testHarness.Bus.Publish(new ReservationReleasedSagaEvent
        {
            OrderId = orderId,
            ProductId = product1,
            ReservationId = rid,
            ReleaseReason = nameof(ReleaseReason.Compensation),
            ReleasedAtUtc = _fakeTimeProvider.GetUtcNow()
        });
        await _sagaHarness.Consumed.Any<ReservationReleasedSagaEvent>();

        await _testHarness.Bus.Publish(new OrderCancelledSagaEvent
        {
            CorrelationId = correlationId,
            OrderId = orderId,
            CancelledAtUtc = _fakeTimeProvider.GetUtcNow()
        });
        (await _sagaHarness.Consumed.Any<OrderCancelledSagaEvent>()).Should().BeTrue();
        (await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null)
            .Should().BeTrue("Compensated is terminal");

        using (new AssertionScope())
        {
            compensated.Should().ContainSingle().Which.Should().Be(1,
                "saga.checkout.compensated increments by 1 on Compensated terminal");
            compensatedTags.Should().ContainSingle().Which.Should().Contain(kv =>
                kv.Key == SagaActivityTags.ErrorCode);
            compensationDurationMs.Should().ContainSingle().Which.Should().BeGreaterThan(0,
                "FakeTimeProvider was advanced 5s between compensation start and finalize");
        }
    }

    private static MeterListener BuildLongCounterListener(
        params (string Name, List<long> Values, List<KeyValuePair<string, object?>[]> Tags)[] subscriptions)
    {
        var listener = new MeterListener();
        var byName = subscriptions.ToDictionary(s => s.Name);

        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name != ApplicationInfo.AppName)
            {
                return;
            }

            if (byName.ContainsKey(instrument.Name))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (byName.TryGetValue(instrument.Name, out var sub))
            {
                sub.Values.Add(measurement);
                sub.Tags.Add(tags.ToArray());
            }
        });
        return listener;
    }

    // ===== helpers (mirror the M3 — M6 test class to keep this test file self-contained) =====

    private async Task PublishInitiated(Guid correlationId, params CheckoutItemSnapshot[] items)
    {
        var sagaEvent = BuildBasketCheckoutInitiated(
            correlationId, Guid.CreateVersion7(), Guid.CreateVersion7(), items);
        await _testHarness.Bus.Publish(sagaEvent);
        (await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null).Should().BeTrue();
    }

    private async Task ReachAwaitingStockReservation(
        Guid correlationId,
        Guid orderId,
        params CheckoutItemSnapshot[] items)
    {
        await PublishInitiated(correlationId, items);
        await _testHarness.Bus.Publish(new OrderCreatedSagaEvent
        {
            CorrelationId = correlationId,
            OrderId = orderId,
            OrderCreatedAtUtc = _fakeTimeProvider.GetUtcNow()
        });
        await _sagaHarness.Consumed.Any<OrderCreatedSagaEvent>();
    }

    private async Task ReachAwaitingPayment(Guid correlationId, Guid orderId, params Guid[] productIds)
    {
        await ReachAwaitingStockReservation(correlationId, orderId,
            productIds.Select(BuildItem).ToArray());
        var state = _sagaHarness.Sagas.Contains(correlationId)!;

        var consumed = 0;
        foreach (var productId in productIds)
        {
            await _testHarness.Bus.Publish(new StockReservedSagaEvent
            {
                OrderId = orderId,
                ProductId = productId,
                ReservationId = GetReservationId(state, productId),
                Quantity = 1,
                ReservedAtUtc = _fakeTimeProvider.GetUtcNow(),
                ExpiresAtUtc = _fakeTimeProvider.GetUtcNow().AddMinutes(15)
            });
            consumed++;
            await WaitForConsumed<StockReservedSagaEvent>(consumed);
        }
    }

    private async Task ReachAwaitingConfirmation(Guid correlationId, Guid orderId, params Guid[] productIds)
    {
        await ReachAwaitingPayment(correlationId, orderId, productIds);
        await _testHarness.Bus.Publish(new PaymentCompletedSagaEvent
        {
            CorrelationId = correlationId,
            PaymentTransactionId = Guid.CreateVersion7(),
            Amount = 9.99m,
            Currency = "USD",
            CompletedAtUtc = _fakeTimeProvider.GetUtcNow()
        });
        await _sagaHarness.Consumed.Any<PaymentCompletedSagaEvent>();
    }

    private async Task ReachCompensatingStockReservations(Guid correlationId, Guid orderId, params Guid[] productIds)
    {
        await ReachAwaitingPayment(correlationId, orderId, productIds);
        await _testHarness.Bus.Publish(new PaymentFailedSagaEvent
        {
            CorrelationId = correlationId,
            ErrorCode = "PAYMENT_FAILED",
            ErrorMessage = "Card declined",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow()
        });
        await _sagaHarness.Consumed.Any<PaymentFailedSagaEvent>();
    }

    private async Task ReachCompensatingPayment(Guid correlationId, Guid orderId, params Guid[] productIds)
    {
        await ReachAwaitingConfirmation(correlationId, orderId, productIds);
        await _testHarness.Bus.Publish(new OrderFailedSagaEvent
        {
            CorrelationId = correlationId,
            OrderId = orderId,
            ErrorCode = "CONFIRMATION_FAILED",
            ErrorMessage = "Internal Ordering error",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow()
        });
        await _sagaHarness.Consumed.Any<OrderFailedSagaEvent>();
    }

    private BasketCheckoutInitiatedSagaEvent BuildBasketCheckoutInitiated(
        Guid correlationId,
        Guid userId,
        Guid paymentMethodId,
        params CheckoutItemSnapshot[] items)
    {
        var basketJson = JsonSerializer.Serialize(items);
        var addr = new
        {
            Street1 = "123 Test St",
            Street2 = (string?)null,
            City = "Testville",
            State = (string?)null,
            PostalCode = "12345",
            CountryCode = "US"
        };
        var addressJson = JsonSerializer.Serialize(addr);

        return new BasketCheckoutInitiatedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            BasketSnapshotJson = basketJson,
            TotalAmount = items.Sum(i => i.LineTotal),
            Currency = "USD",
            PaymentMethodId = paymentMethodId,
            ShippingAddressJson = addressJson,
            BillingAddressJson = addressJson,
            InitiatedAtUtc = _fakeTimeProvider.GetUtcNow()
        };
    }

    private static CheckoutItemSnapshot BuildItem(Guid productId, int qty = 1) =>
        new(productId, "SKU-" + productId.ToString("N")[..6], "Product", qty, 9.99m, "USD", qty * 9.99m);

    private static Guid GetReservationId(CheckoutSagaState state, Guid productId)
    {
        using var doc = JsonDocument.Parse(state.ReservationIdsJson);
        var entry = doc.RootElement.GetProperty(productId.ToString());
        return entry.GetProperty("ReservationId").GetGuid();
    }

    private async Task WaitForConsumed<T>(int expectedCount)
        where T : class
    {
        var deadline = DateTime.UtcNow.Add(DefaultTimeout);
        while (DateTime.UtcNow < deadline)
        {
            var seen = 0;
            await foreach (var _ in _sagaHarness.Consumed.SelectAsync<T>(TestContext.Current.CancellationToken))
            {
                seen++;
                if (seen >= expectedCount)
                {
                    return;
                }
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException(
            $"Timed out waiting for {expectedCount} {typeof(T).Name} messages; observed only some.");
    }

    private sealed record CheckoutItemSnapshot(
        Guid ProductId,
        string Sku,
        string Name,
        int Quantity,
        decimal UnitPriceAmount,
        string UnitPriceCurrency,
        decimal LineTotal);
}
