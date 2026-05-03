using System.Text.Json;
using Avro.Specific;
using Checkout.Sagas;
using Inventory.Reservations;
using MassTransit;
using Microsoft.Extensions.Options;
using Ordering.Orders;
using Payments.Transactions;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;
using SagaOrchestrators.Checkout.CheckoutSaga.Observability;
using SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;
using SagaOrchestrators.Checkout.CheckoutSaga.Schedules;
using SagaOrchestrators.Checkout.CheckoutSaga.Snapshots;
using SagaOrchestrators.Common.Config;
using SagaOrchestrators.Common.Config.Kafka;
using SagaOrchestrators.Common.Extensions;
using SagaDbContext = SagaOrchestrators.Common.Persistence.Database.SagaDbContext;

namespace SagaOrchestrators.Checkout.CheckoutSaga;

/// <summary>
/// MassTransit state machine implementing the Checkout saga - orchestrates the full
/// commercial-commitment flow across Basket -&gt; Ordering -&gt; Inventory -&gt; PaymentProcessingSaga
/// -&gt; Notifications. Eleven states including the abnormal-terminal CompensationStuck per
/// docs/bc-design/checkout-saga.md § 3.
/// </summary>
/// <remarks>
/// M4 landed the event-driven cells of the § 4 transition table. M5 wires the five
/// timeout schedules (<c>OrderCreation</c>/<c>StockReservation</c>/<c>Payment</c>/
/// <c>OrderConfirmation</c>/<c>Compensation</c> per § 7) and the timeout-driven cells of
/// the § 3 transition table (<c>*Timeout.Received</c> rows). Feature-flag wiring
/// (ADR-0014, <c>checkout.payment-then-stock</c>) is deferred to M8; this file ships only
/// the default OFF (stock-then-payment) path.
/// </remarks>
public sealed class CheckoutSagaOrchestrator : MassTransitStateMachine<CheckoutSagaState>
{
    private readonly SagaOptions _sagaOptions;
    private readonly SagaTopicsOptions _topicsOptions;
    private readonly TimeProvider _timeProvider;

    // Happy path states (Initial is MassTransit-implicit)
    public State AwaitingOrderCreation { get; private set; }
    public State AwaitingStockReservation { get; private set; }
    public State AwaitingPayment { get; private set; }
    public State AwaitingConfirmation { get; private set; }
    public State Confirmed { get; private set; }

    // Compensation states
    public State CompensatingStockReservations { get; private set; }
    public State CompensatingPayment { get; private set; }
    public State Compensated { get; private set; }
    public State Failed { get; private set; }
    public State CompensationStuck { get; private set; }

    // Initiator event - missing-instance handling deferred to M4 Initially(...).
    public Event<BasketCheckoutInitiatedSagaEvent> BasketCheckoutInitiatedEvent { get; private set; }

    // Ordering events
    public Event<OrderCreatedSagaEvent> OrderCreatedEvent { get; private set; }
    public Event<OrderFailedSagaEvent> OrderFailedEvent { get; private set; }
    public Event<OrderCancelledSagaEvent> OrderCancelledEvent { get; private set; }
    public Event<OrderConfirmedSagaEvent> OrderConfirmedEvent { get; private set; }

    // Inventory events
    public Event<StockReservedSagaEvent> StockReservedEvent { get; private set; }
    public Event<StockReservationFailedSagaEvent> StockReservationFailedEvent { get; private set; }
    public Event<ReservationReleasedSagaEvent> ReservationReleasedEvent { get; private set; }
    public Event<ReservationConfirmedSagaEvent> ReservationConfirmedEvent { get; private set; }

    // Payments events (delegated via PaymentProcessingSaga)
    public Event<PaymentCompletedSagaEvent> PaymentCompletedEvent { get; private set; }
    public Event<PaymentFailedSagaEvent> PaymentFailedEvent { get; private set; }
    public Event<PaymentRefundedSagaEvent> PaymentRefundedEvent { get; private set; }

    // Timeout schedules (M5 — checkout-saga.md § 7). Tokens persist on CheckoutSagaState
    // so the same saga instance can Unschedule a previously-armed timeout across hops.
    public Schedule<CheckoutSagaState, OrderCreationTimeoutExpired> OrderCreationTimeout { get; private set; } = null!;
    public Schedule<CheckoutSagaState, StockReservationTimeoutExpired> StockReservationTimeout { get; private set; } = null!;
    public Schedule<CheckoutSagaState, PaymentTimeoutExpired> PaymentTimeout { get; private set; } = null!;
    public Schedule<CheckoutSagaState, OrderConfirmationTimeoutExpired> OrderConfirmationTimeout { get; private set; } = null!;
    public Schedule<CheckoutSagaState, CompensationTimeoutExpired> CompensationTimeout { get; private set; } = null!;

    public CheckoutSagaOrchestrator(
        IOptions<SagaOptions> sagaOptions,
        IOptions<SagaTopicsOptions> topicsOptions,
        TimeProvider timeProvider)
    {
        _sagaOptions = sagaOptions.Value;
        _topicsOptions = topicsOptions.Value;
        _timeProvider = timeProvider;

        InstanceState(sagaState => sagaState.CurrentState);

        ConfigureEvents();
        ConfigureSchedules();
        ConfigureStateMachine();
    }

    private void ConfigureStateMachine()
    {
        ConfigureInitialState();
        ConfigureAwaitingOrderCreationState();
        ConfigureAwaitingStockReservationState();
        ConfigureAwaitingPaymentState();
        ConfigureAwaitingConfirmationState();
        ConfigureCompensatingStockReservationsState();
        ConfigureCompensatingPaymentState();

        SetCompletedWhenFinalized();
    }

    /// <summary>
    /// Initial -&gt; AwaitingOrderCreation. Captures the basket-side payload and dispatches
    /// <see cref="CreateOrderCommand"/> to Ordering. § 4 row 1.
    /// </summary>
    private void ConfigureInitialState()
    {
        Initially(
            When(BasketCheckoutInitiatedEvent)
                .Then(ctx =>
                {
                    var saga = ctx.Saga;
                    var message = ctx.Message;
                    saga.CorrelationId = message.CorrelationId;
                    saga.UserId = message.UserId;
                    saga.TotalAmount = message.TotalAmount;
                    saga.Currency = message.Currency;
                    saga.PaymentMethodId = message.PaymentMethodId;
                    saga.BasketSnapshotJson = message.BasketSnapshotJson;
                    saga.ShippingAddressJson = message.ShippingAddressJson;
                    saga.BillingAddressJson = message.BillingAddressJson;
                    saga.InitiatedAtUtc = message.InitiatedAtUtc;
                    saga.ReservationIdsJson = "{}";
                })
                .Activity(x => x.OfType<CheckoutSagaStartedActivity>())
                .PublishToOutbox(
                    _topicsOptions.OrderingOrderCommands,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => BuildCreateOrderCommand(ctx.Saga, _timeProvider.GetUtcNow()))
                .Schedule(OrderCreationTimeout,
                    ctx => new OrderCreationTimeoutExpired { CorrelationId = ctx.Saga.CorrelationId })
                .TransitionTo(AwaitingOrderCreation));
    }

    /// <summary>
    /// AwaitingOrderCreation - § 4 rows 2-3. The OrderCreationTimeout-driven row (4) is M5.
    /// </summary>
    private void ConfigureAwaitingOrderCreationState()
    {
        During(AwaitingOrderCreation,
            When(OrderCreatedEvent)
                .Then(ctx =>
                {
                    var saga = ctx.Saga;
                    var message = ctx.Message;
                    saga.OrderId = message.OrderId;
                    saga.OrderCreatedAtUtc = message.OrderCreatedAtUtc;

                    // § 5.2 fan-out: initialise tracking with one Pending entry per distinct ProductId.
                    var items = DeserializeBasketItems(saga.BasketSnapshotJson);
                    var productGroups = GroupItems(items);

                    var tracking = productGroups.ToDictionary(
                        g => g.ProductId,
                        _ => new ReservationTracking(
                            Status: ReservationStatus.Pending,
                            ReservationId: Guid.CreateVersion7(),
                            ReservedAtUtc: null,
                            ExpiresAtUtc: null));

                    saga.ReservationIdsJson = JsonSerializer.Serialize(tracking);
                    saga.ExpectedReservations = productGroups.Length;
                    saga.PendingReservations = productGroups.Length;
                    saga.StockReservationStartedAtUtc = _timeProvider.GetUtcNow();
                })
                .Activity(x => x.OfType<OrderCreatedActivity>())
                .Unschedule(OrderCreationTimeout)
                .Then(ctx =>
                {
                    var (dbContext, outboxWriter) = GetOutboxDependencies(ctx);
                    var saga = ctx.Saga;
                    var tracking = DeserializeTracking(saga.ReservationIdsJson);
                    var items = DeserializeBasketItems(saga.BasketSnapshotJson);
                    var productGroups = GroupItems(items);

                    foreach (var group in productGroups)
                    {
                        var reservationId = tracking[group.ProductId].ReservationId!.Value;
                        var command = new ReserveStockCommand
                        {
                            CorrelationId = saga.CorrelationId,
                            OrderId = saga.OrderId!.Value,
                            ProductId = group.ProductId,
                            ReservationId = reservationId,
                            Quantity = group.TotalQuantity,
                            RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                        };
                        outboxWriter.AddOutboxMessage(
                            dbContext,
                            _topicsOptions.InventoryReservationCommands,
                            group.ProductId.ToString(),
                            command);
                    }
                })
                .Schedule(StockReservationTimeout,
                    ctx => new StockReservationTimeoutExpired { CorrelationId = ctx.Saga.CorrelationId })
                .TransitionTo(AwaitingStockReservation),
            When(OrderFailedEvent)
                .Then(ctx =>
                {
                    var saga = ctx.Saga;
                    var message = ctx.Message;
                    saga.ErrorCode = message.ErrorCode;
                    saga.ErrorMessage = message.ErrorMessage;
                    saga.FailedAtState = nameof(AwaitingOrderCreation);
                })
                .Activity(x => x.OfType<OrderCreationFailedActivity>())
                .Unschedule(OrderCreationTimeout)
                .Then(ctx => CheckoutSagaMetrics.RecordFailed(
                    ctx.Saga.ErrorCode ?? "UNKNOWN",
                    _timeProvider.GetUtcNow() - ctx.Saga.InitiatedAtUtc))
                .Then(ctx => CheckoutSagaMetrics.DecrementActive())
                .PublishToOutbox(
                    _topicsOptions.CheckoutSagas,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => BuildCheckoutFailedEvent(ctx.Saga, _timeProvider.GetUtcNow()))
                .Then(NullOutAddresses)
                .TransitionTo(Failed)
                .Finalize(),
            When(OrderCreationTimeout.Received)
                .Then(ctx =>
                {
                    var saga = ctx.Saga;
                    saga.ErrorCode = "ORDER_CREATION_TIMEOUT";
                    saga.ErrorMessage = "OrderCreatedEvent not received within OrderCreationSeconds budget";
                    saga.FailedAtState = nameof(AwaitingOrderCreation);
                })
                .Activity(x => x.OfType<OrderCreationTimeoutActivity>())
                // Defensive per § 3 row 4: tell Ordering to fail any silently-accepted Order.
                // OrderId is always null at this point (Ordering's reply hasn't arrived) - we
                // send Guid.Empty + the CorrelationId so Ordering can resolve by correlation id
                // if it implements that path. Mirrors BuildCheckoutFailedEvent's OrderId fallback.
                .PublishToOutbox(
                    _topicsOptions.OrderingOrderCommands,
                    ctx => (ctx.Saga.OrderId ?? ctx.Saga.CorrelationId).ToString(),
                    ctx => new MarkOrderFailedCommand
                    {
                        OrderId = ctx.Saga.OrderId ?? Guid.Empty,
                        CorrelationId = ctx.Saga.CorrelationId,
                        ErrorCode = ctx.Saga.ErrorCode!,
                        ErrorMessage = ctx.Saga.ErrorMessage!,
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .Then(ctx => CheckoutSagaMetrics.RecordFailed(
                    ctx.Saga.ErrorCode ?? "UNKNOWN",
                    _timeProvider.GetUtcNow() - ctx.Saga.InitiatedAtUtc))
                .Then(ctx => CheckoutSagaMetrics.DecrementActive())
                .PublishToOutbox(
                    _topicsOptions.CheckoutSagas,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => BuildCheckoutFailedEvent(ctx.Saga, _timeProvider.GetUtcNow()))
                .Then(NullOutAddresses)
                .TransitionTo(Failed)
                .Finalize());
    }

    /// <summary>
    /// AwaitingStockReservation - § 4 rows 5-6. The StockReservationTimeout-driven row (7) is M5.
    /// </summary>
    private void ConfigureAwaitingStockReservationState()
    {
        During(AwaitingStockReservation,
            When(StockReservedEvent)
                .Then(ctx => UpdateReservationOnReserved(ctx.Saga, ctx.Message))
                .Activity(x => x.OfType<StockReservedActivity>())
                .IfElse(
                    ctx => ctx.Saga.PendingReservations == 0,
                    allReserved => allReserved
                        .Then(ctx =>
                        {
                            var saga = ctx.Saga;
                            saga.StockReservationCompletedAtUtc = _timeProvider.GetUtcNow();
                            saga.PaymentRequestedAtUtc = _timeProvider.GetUtcNow();
                        })
                        .Activity(x => x.OfType<AllStockReservedActivity>())
                        .Unschedule(StockReservationTimeout)
                        .PublishToOutbox(
                            _topicsOptions.PaymentsPayments,
                            ctx => ctx.Saga.CorrelationId.ToString(),
                            ctx => new PaymentRequestedEvent
                            {
                                CorrelationId = ctx.Saga.CorrelationId,
                                OrderId = ctx.Saga.OrderId!.Value,
                                UserId = ctx.Saga.UserId,
                                PaymentMethodId = ctx.Saga.PaymentMethodId,
                                Amount = ctx.Saga.TotalAmount.ToAvroDecimal(4),
                                Currency = ctx.Saga.Currency,
                                IdempotencyKey = ctx.Saga.CorrelationId.ToString(),
                                RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                            })
                        .Schedule(PaymentTimeout,
                            ctx => new PaymentTimeoutExpired { CorrelationId = ctx.Saga.CorrelationId })
                        .TransitionTo(AwaitingPayment),
                    stillPending => stillPending),
            When(StockReservationFailedEvent)
                .Then(ctx =>
                {
                    var saga = ctx.Saga;
                    var message = ctx.Message;
                    var tracking = DeserializeTracking(saga.ReservationIdsJson);
                    if (tracking.TryGetValue(message.ProductId, out var entry))
                    {
                        tracking[message.ProductId] = entry with { Status = ReservationStatus.Failed };
                        saga.ReservationIdsJson = JsonSerializer.Serialize(tracking);
                    }

                    saga.ErrorCode = "STOCK_UNAVAILABLE";
                    saga.ErrorMessage =
                        $"Product {message.ProductId} unavailable: requested {message.RequestedQuantity}, available {message.AvailableQuantity}";
                    saga.FailedAtState = nameof(AwaitingStockReservation);
                })
                .Activity(x => x.OfType<StockReservationFailedActivity>())
                .Unschedule(StockReservationTimeout)
                .Then(ctx => DispatchStockReleaseAndCancelOrder(ctx))
                .Schedule(CompensationTimeout,
                    ctx => new CompensationTimeoutExpired { CorrelationId = ctx.Saga.CorrelationId })
                .TransitionTo(CompensatingStockReservations),
            When(StockReservationTimeout.Received)
                .Then(ctx =>
                {
                    var saga = ctx.Saga;
                    saga.ErrorCode = "STOCK_TIMEOUT";
                    saga.ErrorMessage =
                        $"Not all StockReservedEvents received within budget ({saga.PendingReservations} of {saga.ExpectedReservations} pending)";
                    saga.FailedAtState = nameof(AwaitingStockReservation);
                })
                .Activity(x => x.OfType<StockReservationTimeoutActivity>())
                .Then(ctx => DispatchStockReleaseAndCancelOrder(ctx))
                .Schedule(CompensationTimeout,
                    ctx => new CompensationTimeoutExpired { CorrelationId = ctx.Saga.CorrelationId })
                .TransitionTo(CompensatingStockReservations));
    }

    /// <summary>
    /// AwaitingPayment - § 4 rows 7-8. The PaymentTimeout-driven row (9) is M5.
    /// </summary>
    private void ConfigureAwaitingPaymentState()
    {
        During(AwaitingPayment,
            When(PaymentCompletedEvent)
                .Then(ctx =>
                {
                    var saga = ctx.Saga;
                    var message = ctx.Message;
                    saga.PaymentTransactionId = message.PaymentTransactionId;
                    saga.PaymentCompletedAtUtc = message.CompletedAtUtc;
                    saga.OrderConfirmationRequestedAtUtc = _timeProvider.GetUtcNow();
                })
                .Activity(x => x.OfType<PaymentCompletedCheckoutActivity>())
                .Unschedule(PaymentTimeout)
                .PublishToOutbox(
                    _topicsOptions.OrderingOrderCommands,
                    ctx => ctx.Saga.OrderId!.Value.ToString(),
                    ctx => new ConfirmOrderCommand
                    {
                        OrderId = ctx.Saga.OrderId!.Value,
                        CorrelationId = ctx.Saga.CorrelationId,
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .Then(ctx => DispatchConfirmReservationsForActiveTracking(ctx))
                .Schedule(OrderConfirmationTimeout,
                    ctx => new OrderConfirmationTimeoutExpired { CorrelationId = ctx.Saga.CorrelationId })
                .TransitionTo(AwaitingConfirmation),
            When(PaymentFailedEvent)
                .Then(ctx =>
                {
                    var saga = ctx.Saga;
                    var message = ctx.Message;
                    saga.ErrorCode = message.ErrorCode;
                    saga.ErrorMessage = message.ErrorMessage;
                    saga.FailedAtState = nameof(AwaitingPayment);
                })
                .Activity(x => x.OfType<PaymentFailedCheckoutActivity>())
                .Unschedule(PaymentTimeout)
                .Then(ctx => DispatchStockReleaseAndCancelOrder(ctx))
                .Schedule(CompensationTimeout,
                    ctx => new CompensationTimeoutExpired { CorrelationId = ctx.Saga.CorrelationId })
                .TransitionTo(CompensatingStockReservations),
            When(PaymentTimeout.Received)
                .Then(ctx =>
                {
                    var saga = ctx.Saga;
                    saga.ErrorCode = "PAYMENT_TIMEOUT";
                    saga.ErrorMessage = "PaymentCompletedEvent not received within PaymentSeconds budget";
                    saga.FailedAtState = nameof(AwaitingPayment);
                })
                .Activity(x => x.OfType<PaymentTimeoutCheckoutActivity>())
                .Then(ctx => DispatchStockReleaseAndCancelOrder(ctx))
                .Schedule(CompensationTimeout,
                    ctx => new CompensationTimeoutExpired { CorrelationId = ctx.Saga.CorrelationId })
                .TransitionTo(CompensatingStockReservations));
    }

    /// <summary>
    /// AwaitingConfirmation - § 4 rows 10-12. ReservationConfirmed events stay in state
    /// (informational only). The OrderConfirmationTimeout-driven row (13) is M5.
    /// </summary>
    private void ConfigureAwaitingConfirmationState()
    {
        During(AwaitingConfirmation,
            When(OrderConfirmedEvent)
                .Then(ctx => ctx.Saga.OrderConfirmedAtUtc = ctx.Message.ConfirmedAtUtc)
                .Activity(x => x.OfType<OrderConfirmedActivity>())
                .Unschedule(OrderConfirmationTimeout)
                .PublishToOutbox(
                    _topicsOptions.CheckoutSagas,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => BuildCheckoutCompletedEvent(ctx.Saga))
                .Then(NullOutAddresses)
                .TransitionTo(Confirmed)
                .Finalize(),
            When(ReservationConfirmedEvent)
                .Then(ctx =>
                {
                    var saga = ctx.Saga;
                    var message = ctx.Message;
                    var tracking = DeserializeTracking(saga.ReservationIdsJson);
                    if (tracking.TryGetValue(message.ProductId, out var entry))
                    {
                        tracking[message.ProductId] = entry with { Status = ReservationStatus.Confirmed };
                        saga.ReservationIdsJson = JsonSerializer.Serialize(tracking);
                    }
                })
                .Activity(x => x.OfType<ReservationConfirmedActivity>()),
            When(OrderFailedEvent)
                .Then(ctx =>
                {
                    var saga = ctx.Saga;
                    var message = ctx.Message;
                    saga.ErrorCode = message.ErrorCode;
                    saga.ErrorMessage = message.ErrorMessage;
                    saga.FailedAtState = nameof(AwaitingConfirmation);
                    saga.CompensationTriggered = true;
                    saga.CompensationStartedAtUtc = _timeProvider.GetUtcNow();
                })
                .Activity(x => x.OfType<OrderConfirmationFailedActivity>())
                .Unschedule(OrderConfirmationTimeout)
                .PublishToOutbox(
                    _topicsOptions.PaymentsPaymentCommands,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new RequestRefundCommand
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        PaymentTransactionId = ctx.Saga.PaymentTransactionId!.Value,
                        Reason = ctx.Saga.ErrorMessage ?? "Order confirmation failed",
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .Schedule(CompensationTimeout,
                    ctx => new CompensationTimeoutExpired { CorrelationId = ctx.Saga.CorrelationId })
                .TransitionTo(CompensatingPayment),
            When(OrderConfirmationTimeout.Received)
                .Then(ctx =>
                {
                    var saga = ctx.Saga;
                    saga.ErrorCode = "CONFIRMATION_TIMEOUT";
                    saga.ErrorMessage = "OrderConfirmedEvent not received within OrderConfirmationSeconds budget";
                    saga.FailedAtState = nameof(AwaitingConfirmation);
                    saga.CompensationTriggered = true;
                    saga.CompensationStartedAtUtc = _timeProvider.GetUtcNow();
                })
                .Activity(x => x.OfType<OrderConfirmationTimeoutActivity>())
                .PublishToOutbox(
                    _topicsOptions.PaymentsPaymentCommands,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new RequestRefundCommand
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        PaymentTransactionId = ctx.Saga.PaymentTransactionId!.Value,
                        Reason = ctx.Saga.ErrorMessage ?? "Order confirmation timeout",
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .Schedule(CompensationTimeout,
                    ctx => new CompensationTimeoutExpired { CorrelationId = ctx.Saga.CorrelationId })
                .TransitionTo(CompensatingPayment));
    }

    /// <summary>
    /// CompensatingStockReservations - § 4 rows 13-14. CompensationTimeout-driven row (15) is M5.
    /// </summary>
    private void ConfigureCompensatingStockReservationsState()
    {
        During(CompensatingStockReservations,
            When(ReservationReleasedEvent)
                .Then(ctx => UpdateReservationOnReleased(ctx.Saga, ctx.Message))
                .Activity(x => x.OfType<ReservationReleasedActivity>())
                .IfElse(
                    ctx => ctx.Saga.PendingReleases == 0 && ctx.Saga.OrderCancelledReceived,
                    compensated => compensated
                        .Then(ctx => FinalizeCompensation(ctx.Saga))
                        .Unschedule(CompensationTimeout)
                        .PublishToOutbox(
                            _topicsOptions.CheckoutSagas,
                            ctx => ctx.Saga.CorrelationId.ToString(),
                            ctx => BuildCheckoutFailedEvent(ctx.Saga, _timeProvider.GetUtcNow()))
                        .Then(NullOutAddresses)
                        .TransitionTo(Compensated)
                        .Finalize(),
                    stillWaiting => stillWaiting),
            When(OrderCancelledEvent)
                .Then(ctx => ctx.Saga.OrderCancelledReceived = true)
                .Activity(x => x.OfType<OrderCancelledActivity>())
                .IfElse(
                    ctx => ctx.Saga.PendingReleases == 0 && ctx.Saga.OrderCancelledReceived,
                    compensated => compensated
                        .Then(ctx => FinalizeCompensation(ctx.Saga))
                        .Unschedule(CompensationTimeout)
                        .PublishToOutbox(
                            _topicsOptions.CheckoutSagas,
                            ctx => ctx.Saga.CorrelationId.ToString(),
                            ctx => BuildCheckoutFailedEvent(ctx.Saga, _timeProvider.GetUtcNow()))
                        .Then(NullOutAddresses)
                        .TransitionTo(Compensated)
                        .Finalize(),
                    stillWaiting => stillWaiting),
            When(CompensationTimeout.Received)
                .Then(ctx =>
                {
                    var saga = ctx.Saga;
                    saga.ErrorCode = "COMPENSATION_TIMEOUT";
                    saga.ErrorMessage = "Stock compensation did not complete in time";
                })
                .Activity(x => x.OfType<CompensationTimeoutActivity>())
                .Activity(x => x.OfType<CheckoutStuckActivity>())
                .Then(ctx => CheckoutSagaMetrics.DecrementActive())
                .PublishToOutbox(
                    _topicsOptions.CheckoutSagas,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => BuildCheckoutStuckEvent(
                        ctx.Saga,
                        nameof(CompensatingStockReservations),
                        _timeProvider.GetUtcNow()))
                .Then(NullOutAddresses)
                .TransitionTo(CompensationStuck)
                .Finalize());
    }

    /// <summary>
    /// CompensatingPayment - § 4 row 16 (refund-first per § 6.1). CompensationTimeout-driven
    /// row (17) is M5.
    /// </summary>
    private void ConfigureCompensatingPaymentState()
    {
        During(CompensatingPayment,
            When(PaymentRefundedEvent)
                .Activity(x => x.OfType<PaymentRefundedActivity>())
                .Then(ctx => DispatchStockReleaseAndCancelOrder(ctx))
                // The compensation budget restarts for the stock-release phase per § 6.1
                // refund-then-stock split (refund is done, now bound the release+cancel work).
                .Unschedule(CompensationTimeout)
                .Schedule(CompensationTimeout,
                    ctx => new CompensationTimeoutExpired { CorrelationId = ctx.Saga.CorrelationId })
                .TransitionTo(CompensatingStockReservations),
            When(CompensationTimeout.Received)
                .Then(ctx =>
                {
                    var saga = ctx.Saga;
                    saga.ErrorCode = "COMPENSATION_TIMEOUT";
                    saga.ErrorMessage = "Refund did not complete in time";
                })
                .Activity(x => x.OfType<CompensationTimeoutActivity>())
                .Activity(x => x.OfType<CheckoutStuckActivity>())
                .Then(ctx => CheckoutSagaMetrics.DecrementActive())
                .PublishToOutbox(
                    _topicsOptions.CheckoutSagas,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => BuildCheckoutStuckEvent(
                        ctx.Saga,
                        nameof(CompensatingPayment),
                        _timeProvider.GetUtcNow()))
                .Then(NullOutAddresses)
                .TransitionTo(CompensationStuck)
                .Finalize());
    }

    /// <summary>
    /// Wires correlation rules per docs/bc-design/checkout-saga.md § 4.1. Most events
    /// correlate by <c>CorrelationId</c>. The four Inventory events instead correlate by
    /// <c>OrderId</c> per M3 plan-file § C1 Path B - Inventory's Avro schemas don't yet
    /// carry <c>CorrelationId</c> (§ 8.1 Option B not yet landed); the state-machine
    /// sequence guarantees <c>OrderCreatedSagaEvent</c> precedes any Stock* event so
    /// <c>CheckoutSagaState.OrderId</c> is always set when correlation runs. All intermediate
    /// events use <c>OnMissingInstance(m =&gt; m.Discard())</c> so events arriving for an
    /// already-finalized (or out-of-order) saga are silently dropped - the spec-mandated
    /// divergence from PaymentProcessingSaga, which uses <c>Fault()</c> for some events. The
    /// initiator <see cref="BasketCheckoutInitiatedEvent"/> has no missing-instance policy
    /// because the M4 <c>Initially(...)</c> handler creates the instance on first arrival.
    /// </summary>
    private void ConfigureEvents()
    {
        Event(() => BasketCheckoutInitiatedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Event(() => OrderCreatedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => OrderFailedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => OrderCancelledEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => OrderConfirmedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => StockReservedEvent, e =>
        {
            e.CorrelateBy((state, ctx) => state.OrderId == ctx.Message.OrderId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => StockReservationFailedEvent, e =>
        {
            e.CorrelateBy((state, ctx) => state.OrderId == ctx.Message.OrderId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => ReservationReleasedEvent, e =>
        {
            e.CorrelateBy((state, ctx) => state.OrderId == ctx.Message.OrderId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => ReservationConfirmedEvent, e =>
        {
            e.CorrelateBy((state, ctx) => state.OrderId == ctx.Message.OrderId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => PaymentCompletedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => PaymentFailedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => PaymentRefundedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });
    }

    /// <summary>
    /// Wires the five MassTransit timeout schedules per docs/bc-design/checkout-saga.md § 7.
    /// Delays bind from <c>Saga:CheckoutTimeouts:*</c> in seconds; the SQL message scheduler
    /// (<c>UseSqlMessageScheduler</c>, registered in <c>SagaDependencyInjection</c>) persists
    /// armed timeouts so they survive container restart. Token IDs live on
    /// <see cref="CheckoutSagaState"/> so the in-flight saga can unschedule on the success
    /// path. Each timeout-expired record correlates back by <c>CorrelationId</c>; ADR-0008
    /// guarantees this id threads through the entire workflow.
    /// </summary>
    private void ConfigureSchedules()
    {
        Schedule(() => OrderCreationTimeout, instance => instance.OrderCreationTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromSeconds(_sagaOptions.CheckoutTimeouts.OrderCreationSeconds);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Schedule(() => StockReservationTimeout, instance => instance.StockReservationTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromSeconds(_sagaOptions.CheckoutTimeouts.StockReservationSeconds);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Schedule(() => PaymentTimeout, instance => instance.PaymentTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromSeconds(_sagaOptions.CheckoutTimeouts.PaymentSeconds);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Schedule(() => OrderConfirmationTimeout, instance => instance.OrderConfirmationTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromSeconds(_sagaOptions.CheckoutTimeouts.OrderConfirmationSeconds);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Schedule(() => CompensationTimeout, instance => instance.CompensationTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromSeconds(_sagaOptions.CheckoutTimeouts.CompensationSeconds);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });
    }

    // ----- helpers -----

    private static Dictionary<Guid, ReservationTracking> DeserializeTracking(string json) =>
        string.IsNullOrEmpty(json) || json == "{}"
            ? new Dictionary<Guid, ReservationTracking>()
            : JsonSerializer.Deserialize<Dictionary<Guid, ReservationTracking>>(json)
              ?? new Dictionary<Guid, ReservationTracking>();

    private static IReadOnlyList<BasketItemSnapshot> DeserializeBasketItems(string json) =>
        JsonSerializer.Deserialize<IReadOnlyList<BasketItemSnapshot>>(json) ?? [];

    private static AddressSnapshot? DeserializeAddress(string? json) =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<AddressSnapshot>(json);

    private static ProductGroup[] GroupItems(IEnumerable<BasketItemSnapshot> items) =>
        items.GroupBy(i => i.ProductId)
             .Select(g => new ProductGroup(g.Key, g.Sum(i => i.Quantity)))
             .ToArray();

    /// <summary>
    /// Idempotent fan-in update on <see cref="StockReservedSagaEvent"/>. Skips silently if the
    /// tracking entry has already left <c>Pending</c> (duplicate delivery / stale event guard
    /// per § 5.2 step 3). Recomputes <c>PendingReservations</c> from the dictionary instead of
    /// decrement-by-one to survive retries.
    /// </summary>
    private static void UpdateReservationOnReserved(CheckoutSagaState saga, StockReservedSagaEvent message)
    {
        var tracking = DeserializeTracking(saga.ReservationIdsJson);
        if (!tracking.TryGetValue(message.ProductId, out var entry) || entry.Status != ReservationStatus.Pending)
        {
            return;
        }

        tracking[message.ProductId] = entry with
        {
            Status = ReservationStatus.Reserved,
            ReservationId = message.ReservationId,
            ReservedAtUtc = message.ReservedAtUtc,
            ExpiresAtUtc = message.ExpiresAtUtc
        };

        saga.ReservationIdsJson = JsonSerializer.Serialize(tracking);
        saga.PendingReservations = tracking.Values.Count(t => t.Status == ReservationStatus.Pending);
    }

    /// <summary>
    /// Idempotent update on <see cref="ReservationReleasedSagaEvent"/>. Symmetric with
    /// <see cref="UpdateReservationOnReserved"/>: skips silently if the entry has already left
    /// <c>Reserved</c> (duplicate delivery, stale event, or TTL-driven release that arrived
    /// while compensation was already in progress for that product). Recomputes
    /// <c>PendingReleases</c> from the dictionary.
    /// </summary>
    private static void UpdateReservationOnReleased(CheckoutSagaState saga, ReservationReleasedSagaEvent message)
    {
        var tracking = DeserializeTracking(saga.ReservationIdsJson);
        if (!tracking.TryGetValue(message.ProductId, out var entry) || entry.Status != ReservationStatus.Reserved)
        {
            return;
        }

        tracking[message.ProductId] = entry with { Status = ReservationStatus.Released };
        saga.ReservationIdsJson = JsonSerializer.Serialize(tracking);
        saga.PendingReleases = tracking.Values.Count(t => t.Status == ReservationStatus.Reserved);
    }

    /// <summary>
    /// Common compensation entry: marks compensation triggered, initialises
    /// <c>PendingReleases</c> to the count of currently-reserved entries, publishes
    /// <see cref="ReleaseReservationCommand"/> per active reservation, and publishes
    /// <see cref="CancelOrderCommand"/> if an OrderId is set.
    /// </summary>
    private void DispatchStockReleaseAndCancelOrder<TEvent>(BehaviorContext<CheckoutSagaState, TEvent> ctx)
        where TEvent : class
    {
        var (dbContext, outboxWriter) = GetOutboxDependencies(ctx);
        var saga = ctx.Saga;
        var tracking = DeserializeTracking(saga.ReservationIdsJson);
        var activeReservations = tracking
            .Where(kv => kv.Value.Status == ReservationStatus.Reserved && kv.Value.ReservationId is not null)
            .ToArray();

        saga.CompensationTriggered = true;
        saga.CompensationStartedAtUtc ??= _timeProvider.GetUtcNow();
        saga.PendingReleases = activeReservations.Length;

        foreach (var (productId, entry) in activeReservations)
        {
            var release = new ReleaseReservationCommand
            {
                CorrelationId = saga.CorrelationId,
                ProductId = productId,
                ReservationId = entry.ReservationId!.Value,
                ReleaseReason = ReleaseReason.Compensation,
                RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
            };
            outboxWriter.AddOutboxMessage(
                dbContext,
                _topicsOptions.InventoryReservationCommands,
                productId.ToString(),
                release);
        }

        if (saga.OrderId is { } orderId)
        {
            var cancel = new CancelOrderCommand
            {
                OrderId = orderId,
                CorrelationId = saga.CorrelationId,
                Reason = saga.ErrorMessage ?? "Checkout saga compensation",
                RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
            };
            outboxWriter.AddOutboxMessage(
                dbContext,
                _topicsOptions.OrderingOrderCommands,
                orderId.ToString(),
                cancel);
        }
    }

    private void DispatchConfirmReservationsForActiveTracking<TEvent>(
        BehaviorContext<CheckoutSagaState, TEvent> ctx)
        where TEvent : class
    {
        var (dbContext, outboxWriter) = GetOutboxDependencies(ctx);
        var saga = ctx.Saga;
        var tracking = DeserializeTracking(saga.ReservationIdsJson);
        var activeReservations = tracking
            .Where(kv => kv.Value.Status == ReservationStatus.Reserved && kv.Value.ReservationId is not null)
            .ToArray();

        foreach (var (productId, entry) in activeReservations)
        {
            var confirm = new ConfirmReservationCommand
            {
                CorrelationId = saga.CorrelationId,
                ProductId = productId,
                ReservationId = entry.ReservationId!.Value,
                RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
            };
            outboxWriter.AddOutboxMessage(
                dbContext,
                _topicsOptions.InventoryReservationCommands,
                productId.ToString(),
                confirm);
        }
    }

    private void FinalizeCompensation(CheckoutSagaState saga)
    {
        var now = _timeProvider.GetUtcNow();
        saga.CompensationCompletedAtUtc = now;
        var compDuration = saga.CompensationStartedAtUtc is { } started ? now - started : TimeSpan.Zero;
        var totalDuration = now - saga.InitiatedAtUtc;
        CheckoutSagaMetrics.RecordCompensated(saga.ErrorCode ?? "UNKNOWN", totalDuration, compDuration);
        CheckoutSagaMetrics.DecrementActive();
    }

    private static void NullOutAddresses<TEvent>(BehaviorContext<CheckoutSagaState, TEvent> ctx)
        where TEvent : class
    {
        // ADR-0011 PII retention rule: addresses live only for the saga's lifetime.
        ctx.Saga.ShippingAddressJson = null;
        ctx.Saga.BillingAddressJson = null;
    }

    /// <summary>
    /// Resolves the scoped <see cref="SagaDbContext"/> + <see cref="IOutboxWriter"/> from the
    /// behaviour context's service scope - same mechanism as
    /// <see cref="SagaOutboxExtensions"/>.PublishToOutbox internally. Used by the loop-based
    /// fan-out paths where the fluent <c>.PublishToOutbox(...)</c> single-message helper does
    /// not fit.
    /// </summary>
    private static (SagaDbContext DbContext, IOutboxWriter OutboxWriter) GetOutboxDependencies<TEvent>(
        BehaviorContext<CheckoutSagaState, TEvent> context)
        where TEvent : class
    {
        if (!context.TryGetPayload<IServiceScope>(out var scope))
        {
            throw new InvalidOperationException(
                "Unable to resolve IServiceScope from the behavior context. Ensure the saga is "
                + "configured with an Entity Framework repository.");
        }

        var dbContext = scope.ServiceProvider.GetRequiredService<SagaDbContext>();
        var outboxWriter = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
        return (dbContext, outboxWriter);
    }

    // ----- outbox payload builders -----

    private CreateOrderCommand BuildCreateOrderCommand(CheckoutSagaState saga, DateTimeOffset now)
    {
        var items = DeserializeBasketItems(saga.BasketSnapshotJson);
        var shipping = DeserializeAddress(saga.ShippingAddressJson) ?? throw new InvalidOperationException(
            $"Saga {saga.CorrelationId} missing ShippingAddressJson on Initial transition.");
        var billing = DeserializeAddress(saga.BillingAddressJson) ?? throw new InvalidOperationException(
            $"Saga {saga.CorrelationId} missing BillingAddressJson on Initial transition.");

        return new CreateOrderCommand
        {
            CorrelationId = saga.CorrelationId,
            BuyerId = saga.UserId,
            Items = items.Select(MapBasketItem).ToList<CreateOrderItem>(),
            ShippingAddress = MapAddress(shipping),
            BillingAddress = MapAddress(billing),
            PaymentMethodId = saga.PaymentMethodId,
            RequestedAtUtc = now.UtcDateTime
        };
    }

    private static CreateOrderItem MapBasketItem(BasketItemSnapshot item) => new()
    {
        ProductId = item.ProductId,
        Sku = item.Sku,
        Name = item.Name,
        UnitPriceAmount = item.UnitPriceAmount.ToAvroDecimal(4),
        UnitPriceCurrency = item.UnitPriceCurrency,
        Quantity = item.Quantity
    };

    private static OrderAddress MapAddress(AddressSnapshot snapshot) => new()
    {
        Street1 = snapshot.Street1,
        Street2 = snapshot.Street2!,
        City = snapshot.City,
        State = snapshot.State!,
        PostalCode = snapshot.PostalCode,
        CountryCode = snapshot.CountryCode
    };

    private static CheckoutCompletedEvent BuildCheckoutCompletedEvent(CheckoutSagaState saga) => new()
    {
        CorrelationId = saga.CorrelationId,
        OrderId = saga.OrderId!.Value,
        UserId = saga.UserId,
        PaymentTransactionId = saga.PaymentTransactionId!.Value,
        ReservationIdsJson = saga.ReservationIdsJson,
        TotalAmount = saga.TotalAmount.ToAvroDecimal(4),
        Currency = saga.Currency,
        InitiatedAtUtc = saga.InitiatedAtUtc.UtcDateTime,
        ConfirmedAtUtc = (saga.OrderConfirmedAtUtc ?? DateTimeOffset.MinValue).UtcDateTime
    };

    private static CheckoutFailedEvent BuildCheckoutFailedEvent(CheckoutSagaState saga, DateTimeOffset now) => new()
    {
        CorrelationId = saga.CorrelationId,
        OrderId = saga.OrderId ?? Guid.Empty,
        UserId = saga.UserId,
        ErrorCode = saga.ErrorCode ?? "UNKNOWN",
        ErrorMessage = saga.ErrorMessage ?? string.Empty,
        FailedAtState = saga.FailedAtState ?? string.Empty,
        CompensationTriggered = saga.CompensationTriggered,
        InitiatedAtUtc = saga.InitiatedAtUtc.UtcDateTime,
        FailedAtUtc = now.UtcDateTime
    };

    private static CheckoutStuckEvent BuildCheckoutStuckEvent(
        CheckoutSagaState saga,
        string lastState,
        DateTimeOffset now) => new()
        {
            CorrelationId = saga.CorrelationId,
            OrderId = saga.OrderId ?? Guid.Empty,
            UserId = saga.UserId,
            LastState = lastState,
            ErrorCode = saga.ErrorCode ?? "COMPENSATION_TIMEOUT",
            ErrorMessage = saga.ErrorMessage ?? string.Empty,
            FailureReason = saga.ErrorMessage ?? "Compensation did not complete in time",
            StuckSinceUtc = (saga.CompensationStartedAtUtc ?? saga.InitiatedAtUtc).UtcDateTime,
            InitiatedAtUtc = saga.InitiatedAtUtc.UtcDateTime,
            EmittedAtUtc = now.UtcDateTime
        };

    private readonly record struct ProductGroup(Guid ProductId, int TotalQuantity);
}
