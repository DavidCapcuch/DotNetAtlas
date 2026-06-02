using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SagaOrchestrators.Payments.PaymentProcessingSaga;

namespace SagaOrchestrators.Common.Persistence.Database;

public sealed class PaymentProcessingSagaStateMap :
    SagaClassMap<PaymentProcessingSagaState>
{
    protected override void Configure(EntityTypeBuilder<PaymentProcessingSagaState> entity, ModelBuilder model)
    {
        entity.ToTable("payment_processing_saga_state", SagaDbContext.DefaultSchemaName,
            t => t.HasComment("Saga state for payment processing orchestration."));

        // Primary key - configured by MassTransit SagaClassMap base
        entity.Property(x => x.CorrelationId)
            .HasComment("Unique correlation ID for the payment saga")
            .ValueGeneratedNever();

        // State
        entity.Property(x => x.CurrentState)
            .HasComment("Current state of the saga state machine")
            .HasMaxLength(64)
            .IsRequired();

        entity.HasIndex(x => x.CurrentState)
            .HasDatabaseName("ix_payment_processing_saga_state_current_state");

        // Ordering aggregate id this payment is attached to. Frozen at saga start
        // (the Checkout saga always creates the Order before requesting payment).
        // Indexed for admin lookups of "all payment sagas for order X".
        entity.Property(x => x.OrderId)
            .HasComment("Ordering aggregate id this payment is attached to. Frozen at saga start.");

        entity.HasIndex(x => x.OrderId)
            .HasDatabaseName("ix_payment_processing_saga_state_order_id");

        // User and Payment Method
        entity.Property(x => x.UserId)
            .HasComment("User initiating the payment");

        entity.HasIndex(x => x.UserId)
            .HasDatabaseName("ix_payment_processing_saga_state_user_id");

        entity.Property(x => x.PaymentMethodId)
            .HasComment("Gateway-issued opaque payment-method token (Stripe 'pm_*', Adyen alphanumeric, …); 1-64 chars. Changed from uuid in the Wave-1 closeout C-2 fix.")
            .HasMaxLength(64)
            .IsRequired();

        // Payment details
        entity.Property(x => x.Amount)
            .HasComment("Payment amount")
            .HasPrecision(19, 4);

        entity.Property(x => x.Currency)
            .HasComment("ISO 4217 currency code")
            .HasMaxLength(3)
            .IsRequired();

        entity.Property(x => x.IdempotencyKey)
            .HasComment("Idempotency key to prevent duplicate processing")
            .HasMaxLength(128)
            .IsRequired();

        entity.HasIndex(x => x.IdempotencyKey)
            .HasDatabaseName("ux_payment_processing_saga_state_idempotency_key")
            .IsUnique();

        // Authorization details
        entity.Property(x => x.AuthorizationId)
            .HasComment("Authorization ID from payment provider")
            .HasMaxLength(256);

        entity.Property(x => x.AuthorizationExpiresAtUtc)
            .HasComment("UTC timestamp when authorization expires");

        entity.Property(x => x.PaymentTransactionId)
            .HasComment("Payment transaction ID after capture");

        // Timestamps
        entity.Property(x => x.InitiatedAtUtc)
            .HasComment("UTC timestamp when payment was initiated");

        entity.Property(x => x.CreatedUtc)
            .HasComment("UTC timestamp when saga was created");

        entity.Property(x => x.LastModifiedUtc)
            .HasComment("UTC timestamp when saga was last updated");

        entity.Property(x => x.AuthorizedAtUtc)
            .HasComment("UTC timestamp when authorization completed");

        entity.Property(x => x.CapturedAtUtc)
            .HasComment("UTC timestamp when capture completed");

        // Retry tracking
        entity.Property(x => x.AuthorizationRetryCount)
            .HasComment("Number of authorization retry attempts")
            .HasDefaultValue(0);

        entity.Property(x => x.CaptureRetryCount)
            .HasComment("Number of capture retry attempts")
            .HasDefaultValue(0);

        // Error handling
        entity.Property(x => x.ErrorCode)
            .HasComment("Error code for categorized failure handling")
            .HasMaxLength(64);

        entity.Property(x => x.ErrorMessage)
            .HasComment("Error message if failed")
            .HasMaxLength(2048);

        // Compensation
        entity.Property(x => x.CompensationTriggered)
            .HasComment("Whether compensation has been triggered");

        entity.Property(x => x.CompensationCompletedAtUtc)
            .HasComment("UTC timestamp when compensation completed");

        // Scheduler tokens
        entity.Property(x => x.AuthorizationTimeoutTokenId)
            .HasComment("Token ID for authorization timeout scheduler - set when schedule is active");

        entity.Property(x => x.CaptureApprovalTimeoutTokenId)
            .HasComment("Token ID for capture-approval wait-state timeout scheduler - set when schedule is active");

        entity.Property(x => x.CaptureTimeoutTokenId)
            .HasComment("Token ID for capture timeout scheduler - set when schedule is active");

        entity.Property(x => x.VoidTimeoutTokenId)
            .HasComment("Token ID for void timeout scheduler - set when schedule is active");

        entity.Property(s => s.RowVersion)
            .IsRowVersion()
            .HasComment("Optimistic concurrency token.");

        entity.HasIndex(x => new
        {
            x.CurrentState,
            CreatedAtUtc = x.CreatedUtc
        })
            .HasDatabaseName("ix_payment_processing_saga_state_state_created");

        // Index for stuck saga health check queries
        entity.HasIndex(x => new
        {
            x.CurrentState,
            LastUpdatedAtUtc = x.LastModifiedUtc
        })
            .HasDatabaseName("ix_payment_processing_saga_state_state_last_updated");
    }
}
