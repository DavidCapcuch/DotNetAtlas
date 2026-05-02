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
        entity.ToTable("CheckoutSagaState", SagaDbContext.DefaultSchemaName,
            t => t.HasComment("Saga state for the checkout orchestration."));

        // Primary key - configured by MassTransit SagaClassMap base
        entity.Property(x => x.CorrelationId)
            .HasComment("Workflow correlation id - equals BasketCheckoutInitiatedEvent.BasketCorrelationId (ADR-0008).")
            .ValueGeneratedNever();

        // State
        entity.Property(x => x.CurrentState)
            .HasComment("Current state of the saga state machine.")
            .HasMaxLength(64)
            .IsRequired();

        entity.HasIndex(x => x.CurrentState)
            .HasDatabaseName("IX_CheckoutSagaState_CurrentState");

        // Buyer / user data
        entity.Property(x => x.UserId)
            .HasComment("User initiating checkout. Becomes Ordering's BuyerId.");

        entity.HasIndex(x => x.UserId)
            .HasDatabaseName("IX_CheckoutSagaState_UserId");

        entity.Property(x => x.TotalAmount)
            .HasComment("Sum of basket line totals captured at checkout initiation.")
            .HasPrecision(19, 4);

        entity.Property(x => x.Currency)
            .HasComment("ISO 4217 currency code.")
            .HasMaxLength(3)
            .IsRequired();

        entity.Property(x => x.PaymentMethodId)
            .HasComment("Saved payment method id - passed through to PaymentProcessingSaga.");

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
        entity.Property(x => x.OrderId)
            .HasComment("Ordering aggregate id assigned after OrderCreatedEvent. Null until OrderCreated arrives.");

        entity.HasIndex(x => x.OrderId)
            .HasDatabaseName("IX_CheckoutSagaState_OrderId");

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
            .HasComment("UTC timestamp when PaymentRequestedEvent was emitted.");

        entity.Property(x => x.PaymentCompletedAtUtc)
            .HasComment("UTC timestamp when PaymentCompletedSagaEvent was received.");

        // Confirmation
        entity.Property(x => x.OrderConfirmationRequestedAtUtc)
            .HasComment("UTC timestamp when ConfirmOrderCommand was dispatched.");

        entity.Property(x => x.OrderConfirmedAtUtc)
            .HasComment("UTC timestamp when OrderConfirmedSagaEvent arrived.");

        // Compensation
        entity.Property(x => x.CompensationStartedAtUtc)
            .HasComment("UTC timestamp at first transition into any Compensating* state.");

        entity.Property(x => x.CompensationCompletedAtUtc)
            .HasComment("UTC timestamp at transition into Compensated.");

        entity.Property(x => x.CompensationTriggered)
            .HasComment("Set true on the first Compensating* transition.");

        entity.Property(x => x.ErrorCode)
            .HasComment("Categorised failure code (e.g., STOCK_UNAVAILABLE, PAYMENT_FAILED).")
            .HasMaxLength(64);

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
            .HasDatabaseName("IX_CheckoutSagaState_State_Created");

        // Index for stuck saga health check queries
        entity.HasIndex(x => new
        {
            x.CurrentState,
            LastUpdatedAtUtc = x.LastModifiedUtc
        })
            .HasDatabaseName("IX_CheckoutSagaState_State_LastUpdated");
    }
}
