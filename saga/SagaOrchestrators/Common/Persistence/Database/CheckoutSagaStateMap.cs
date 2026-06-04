using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SagaOrchestrators.Checkout.CheckoutSaga;

namespace SagaOrchestrators.Common.Persistence.Database;

public sealed class CheckoutSagaStateMap :
    SagaClassMap<CheckoutSagaState>
{
    protected override void Configure(EntityTypeBuilder<CheckoutSagaState> entity, ModelBuilder model)
    {
        entity.ToTable("checkout_saga_state", SagaDbContext.DefaultSchemaName,
            t => t.HasComment("Saga state for the checkout orchestration."));

        // Primary key - configured by MassTransit SagaClassMap base
        entity.Property(x => x.CorrelationId)
            .HasComment("MassTransit saga instance id (ISaga.CorrelationId); equals the pre-assigned OrderId (ADR-0029).")
            .ValueGeneratedNever();

        // State
        entity.Property(x => x.CurrentState)
            .HasComment("Current state of the saga state machine.")
            .HasMaxLength(64)
            .IsRequired();

        entity.HasIndex(x => x.CurrentState)
            .HasDatabaseName("ix_checkout_saga_state_current_state");

        // Buyer / user data
        entity.Property(x => x.UserId)
            .HasComment("User initiating checkout. Becomes Ordering's BuyerId.");

        entity.HasIndex(x => x.UserId)
            .HasDatabaseName("ix_checkout_saga_state_user_id");

        entity.Property(x => x.TotalAmount)
            .HasComment("Sum of basket line totals captured at checkout initiation.")
            .HasPrecision(19, 4);

        entity.Property(x => x.Currency)
            .HasComment("ISO 4217 currency code.")
            .HasMaxLength(3)
            .IsRequired();

        entity.Property(x => x.PaymentMethodId)
            .HasComment("Saved payment method id (Guid). Stored as uuid because Basket + Ordering wire shapes still use Guid; CheckoutSaga string-encodes it only at the Payments-emit boundary (C-2 closeout — Payments-side schema changed, upstream BC wire shapes deferred).");

        entity.Property(x => x.BasketSnapshotJson)
            .HasComment("Serialized basket line snapshot (immutable for the saga's lifetime).")
            .HasColumnType("jsonb")
            .IsRequired();

        // Addresses - PII per ADR-0011, nulled out on terminal transitions.
        entity.Property(x => x.ShippingAddressJson)
            .HasComment("Serialized shipping Address value object. Nulled out on terminal per ADR-0011.")
            .HasColumnType("jsonb");

        entity.Property(x => x.BillingAddressJson)
            .HasComment("Serialized billing Address value object. Nulled out on terminal per ADR-0011.")
            .HasColumnType("jsonb");

        entity.Property(x => x.InitiatedAtUtc)
            .HasComment("UTC timestamp when the saga was initiated (copied from the Basket event).");

        // Ordering side
        entity.Property(x => x.OrderCreatedAtUtc)
            .HasComment("UTC timestamp when Ordering reported the order created.");

        // Inventory side
        entity.Property(x => x.ReservationIdsJson)
            .HasComment("Serialized per-ProductId reservation tracking dictionary.")
            .HasColumnType("jsonb")
            .IsRequired();

        entity.Property(x => x.ExpectedReservations)
            .HasComment("Number of distinct ProductIds in the basket - target reservation count.")
            .HasDefaultValue(0);

        entity.Property(x => x.PendingReservations)
            .HasComment("Decremented on each StockReservedSagaEvent. Zero triggers AwaitingPayment.")
            .HasDefaultValue(0);

        entity.Property(x => x.StockReservationStartedAtUtc)
            .HasComment("UTC timestamp when stock reservation fan-out began.");

        entity.Property(x => x.StockReservationCompletedAtUtc)
            .HasComment("UTC timestamp when all reservations completed.");

        // Payment side
        entity.Property(x => x.PaymentTransactionId)
            .HasComment("Payment transaction id from PaymentProcessingSaga. Required for refund compensation.");

        entity.Property(x => x.PaymentRequestedAtUtc)
            .HasComment("UTC timestamp when RequestPaymentCommand was emitted to payments.payment-commands (per ADR-0023; renamed from PaymentRequestedEvent).");

        entity.Property(x => x.PaymentCompletedAtUtc)
            .HasComment("UTC timestamp when PaymentCompletedSagaEvent was received.");

        // Confirmation
        entity.Property(x => x.OrderConfirmationRequestedAtUtc)
            .HasComment("UTC timestamp when ConfirmOrderCommand was dispatched.");

        entity.Property(x => x.OrderConfirmedAtUtc)
            .HasComment("UTC timestamp when OrderConfirmedSagaEvent arrived.");

        // Compensation
        entity.Property(x => x.PendingReleases)
            .HasComment("Decremented on each ReservationReleasedSagaEvent during compensation. Zero AND OrderCancelledReceived=true gates the transition to Compensated.")
            .HasDefaultValue(0);

        entity.Property(x => x.OrderCancelledReceived)
            .HasComment("True once OrderCancelledSagaEvent has been observed during compensation - gates the transition to Compensated.")
            .HasDefaultValue(false);

        entity.Property(x => x.CompensationStartedAtUtc)
            .HasComment("UTC timestamp at first transition into any Compensating* state.");

        entity.Property(x => x.CompensationCompletedAtUtc)
            .HasComment("UTC timestamp at transition into Compensated.");

        entity.Property(x => x.CompensationTriggered)
            .HasComment("Set true on the first Compensating* transition.");

        entity.Property(x => x.ErrorCode)
            .HasComment("Categorised failure code (e.g., STOCK_UNAVAILABLE, PAYMENT_FAILED).")
            // 100, not a saga-local choice: this column also persists codes FORWARDED verbatim from
            // upstream events (Ordering's OrderFailedEvent, Payments' PaymentFailedEvent — see the
            // `saga.ErrorCode = message.ErrorCode` assignments in CheckoutSagaOrchestrator), so it must
            // hold the longest code any producer can emit. Ordering caps its codes at
            // FailureInfo.MaxErrorCodeLength = 100; a narrower column would reject the insert and fail
            // the saga state write. Kept in lockstep with that cap.
            .HasMaxLength(100);

        entity.Property(x => x.ErrorMessage)
            .HasComment("Human-readable failure message.")
            .HasMaxLength(2048);

        entity.Property(x => x.FailedAtState)
            .HasComment("Name of the state when failure first occurred. Aids ops forensics.")
            .HasMaxLength(64);

        // Audit
        entity.Property(x => x.CreatedUtc)
            .HasComment("UTC timestamp when saga row was created.");

        entity.Property(x => x.LastModifiedUtc)
            .HasComment("UTC timestamp when saga row was last mutated.");

        // Scheduler tokens
        entity.Property(x => x.OrderCreationTimeoutTokenId)
            .HasComment("Token ID for the order-creation timeout scheduler - set when schedule is active.");

        entity.Property(x => x.StockReservationTimeoutTokenId)
            .HasComment("Token ID for the stock-reservation timeout scheduler - set when schedule is active.");

        entity.Property(x => x.PaymentTimeoutTokenId)
            .HasComment("Token ID for the payment timeout scheduler - set when schedule is active.");

        entity.Property(x => x.OrderConfirmationTimeoutTokenId)
            .HasComment("Token ID for the order-confirmation timeout scheduler - set when schedule is active.");

        entity.Property(x => x.CompensationTimeoutTokenId)
            .HasComment("Token ID for the compensation timeout scheduler - set when schedule is active.");

        entity.Property(s => s.RowVersion)
            .IsRowVersion()
            .HasComment("Optimistic concurrency token.");

        entity.HasIndex(x => new
        {
            x.CurrentState,
            CreatedAtUtc = x.CreatedUtc
        })
            .HasDatabaseName("ix_checkout_saga_state_state_created");

        // Index for stuck saga health check queries
        entity.HasIndex(x => new
        {
            x.CurrentState,
            LastUpdatedAtUtc = x.LastModifiedUtc
        })
            .HasDatabaseName("ix_checkout_saga_state_state_last_updated");
    }
}
