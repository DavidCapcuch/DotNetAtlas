using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SagaOrchestrators.Finance.PaymentProcessingSaga;

namespace SagaOrchestrators.Persistence.Database;

public sealed class PaymentProcessingSagaStateMap :
    SagaClassMap<PaymentProcessingSagaState>
{
    protected override void Configure(EntityTypeBuilder<PaymentProcessingSagaState> entity, ModelBuilder model)
    {
        entity.ToTable("PaymentProcessingSagaState", SagaDbContext.DefaultSchemaName,
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
            .HasDatabaseName("IX_PaymentSagaState_CurrentState");

        // User and Payment Method
        entity.Property(x => x.UserId)
            .HasComment("User initiating the payment");

        entity.HasIndex(x => x.UserId)
            .HasDatabaseName("IX_PaymentSagaState_UserId");

        entity.Property(x => x.PaymentMethodId)
            .HasComment("ID of the saved payment method");

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
            .HasDatabaseName("IX_PaymentSagaState_IdempotencyKey")
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

        entity.Property(x => x.CaptureTimeoutTokenId)
            .HasComment("Token ID for capture timeout scheduler - set when schedule is active");

        entity.Property(x => x.VoidTimeoutTokenId)
            .HasComment("Token ID for void timeout scheduler - set when schedule is active");

        entity.Property(x => x.RefundTimeoutTokenId)
            .HasComment("Token ID for refund timeout scheduler - set when schedule is active");

        entity.Property(x => x.SuccessFinalizationTimeoutTokenId)
            .HasComment("Token ID for success finalization timeout scheduler - set when schedule is active");

        entity.Property(s => s.RowVersion)
            .IsRowVersion()
            .HasComment("Optimistic concurrency token.");

        entity.HasIndex(x => new
        {
            x.CurrentState,
            CreatedAtUtc = x.CreatedUtc
        })
            .HasDatabaseName("IX_PaymentSagaState_State_Created");

        // Index for stuck saga health check queries
        entity.HasIndex(x => new
        {
            x.CurrentState,
            LastUpdatedAtUtc = x.LastModifiedUtc
        })
            .HasDatabaseName("IX_PaymentSagaState_State_LastUpdated");
    }
}
