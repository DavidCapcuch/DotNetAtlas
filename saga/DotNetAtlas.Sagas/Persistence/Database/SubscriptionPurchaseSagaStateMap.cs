using DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetAtlas.Sagas.Persistence.Database;

public sealed class SubscriptionPurchaseSagaStateMap :
    SagaClassMap<SubscriptionPurchaseSagaState>
{
    protected override void Configure(EntityTypeBuilder<SubscriptionPurchaseSagaState> entity, ModelBuilder model)
    {
        entity.ToTable("SubscriptionPurchaseSagaState", SubscriptionSagaDbContext.DefaultSchemaName,
            t => t.HasComment("Saga state for subscription purchase orchestration."));

        // Primary key - configured by MassTransit SagaClassMap base
        entity.Property(x => x.CorrelationId)
            .HasComment("Unique correlation ID (also PaymentTransactionId)")
            .ValueGeneratedNever();

        // State
        entity.Property(x => x.CurrentState)
            .HasComment("Current state of the saga state machine")
            .HasMaxLength(64)
            .IsRequired();

        entity.HasIndex(x => x.CurrentState)
            .HasDatabaseName("IX_SubscriptionPurchaseSagaState_CurrentState");

        // Business properties
        entity.Property(x => x.UserId)
            .HasComment("User who purchased the subscription");

        entity.HasIndex(x => x.UserId)
            .HasDatabaseName("IX_SubscriptionPurchaseSagaState_UserId");

        entity.Property(x => x.PaymentMethodId)
            .HasComment("ID of the saved payment method");

        entity.Property(x => x.SubscriptionTier)
            .HasComment("Subscription tier (Pro, Ultra)")
            .HasConversion<int>();

        entity.Property(x => x.DurationDays)
            .HasComment("Subscription duration in days");

        entity.Property(x => x.Amount)
            .HasComment("Payment amount")
            .HasPrecision(18, 4);

        entity.Property(x => x.Currency)
            .HasComment("ISO 4217 currency code")
            .HasMaxLength(3)
            .IsRequired();

        entity.Property(x => x.IdempotencyKey)
            .HasComment("Idempotency key to prevent duplicate purchases")
            .HasMaxLength(128)
            .IsRequired();

        entity.HasIndex(x => x.IdempotencyKey)
            .HasDatabaseName("IX_SubscriptionPurchaseSagaState_IdempotencyKey")
            .IsUnique();

        entity.Property(x => x.PaymentTransactionId)
            .HasComment("Payment transaction ID (set after payment completes)");

        // Timestamps
        entity.Property(x => x.PurchaseInitiatedAtUtc)
            .HasComment("UTC timestamp when purchase was initiated");

        entity.Property(x => x.CreatedAtUtc)
            .HasComment("UTC timestamp when saga was created");

        entity.Property(x => x.LastUpdatedAtUtc)
            .HasComment("UTC timestamp when saga was last updated");

        entity.Property(x => x.PaymentCompletedAtUtc)
            .HasComment("UTC timestamp when payment completed (null if not completed)");

        entity.Property(x => x.ActivationCompletedAtUtc)
            .HasComment("UTC timestamp when activation completed (null if not completed)");

        // Error handling
        entity.Property(x => x.RetryCount)
            .HasComment("Number of retry attempts")
            .HasDefaultValue(0);

        entity.Property(x => x.ErrorMessage)
            .HasComment("Error message if failed")
            .HasMaxLength(2048);

        entity.Property(x => x.ErrorCode)
            .HasComment("Error code for categorized failure handling")
            .HasMaxLength(64);

        // Compensation
        entity.Property(x => x.CompensationTriggered)
            .HasComment("Whether compensation (refund) has been triggered")
            .HasDefaultValue(false);

        entity.Property(x => x.CompensationCompletedAtUtc)
            .HasComment("UTC timestamp when compensation completed");

        // Scheduler tokens
        entity.Property(x => x.PaymentTimeoutTokenId)
            .HasComment("Token ID for payment timeout scheduler");

        entity.Property(x => x.ActivationTimeoutTokenId)
            .HasComment("Token ID for activation timeout scheduler");

        entity.Property(x => x.CompensationTimeoutTokenId)
            .HasComment("Token ID for compensation timeout scheduler");

        // Optimistic concurrency - using Version property from ISagaVersion
        entity.Property(x => x.Version)
            .HasComment("Version for optimistic concurrency control")
            .IsConcurrencyToken();

        // Composite index for common queries
        entity.HasIndex(x => new
        {
            x.CurrentState,
            x.CreatedAtUtc
        })
            .HasDatabaseName("IX_SubscriptionPurchaseSagaState_State_Created");

        // Index for stuck saga health check queries (filter excludes terminal states)
        entity.HasIndex(x => new
        {
            x.CurrentState,
            x.LastUpdatedAtUtc
        })
            .HasDatabaseName("IX_SubscriptionPurchaseSagaState_State_LastUpdated")
            .HasFilter(
                "[CurrentState] <> 'PaymentFailed' AND [CurrentState] <> 'ActivationCompleted' AND [CurrentState] <> 'ActivationFailed' AND [CurrentState] <> 'CompensationCompleted' AND [CurrentState] <> 'CompensationFailed'");
    }
}
