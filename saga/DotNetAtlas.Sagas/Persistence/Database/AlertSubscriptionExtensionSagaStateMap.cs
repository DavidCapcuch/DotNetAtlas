using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetAtlas.Sagas.Persistence.Database;

public sealed class AlertSubscriptionExtensionSagaStateMap :
    SagaClassMap<AlertSubscriptionExtensionSagaState>
{
    protected override void Configure(EntityTypeBuilder<AlertSubscriptionExtensionSagaState> entity, ModelBuilder model)
    {
        entity.ToTable("AlertSubscriptionExtensionSagaState", SagaDbContext.DefaultSchemaName,
            t => t.HasComment("Saga state for alert subscription extension orchestration."));

        entity.Property(x => x.CorrelationId)
            .HasComment("PK - Unique correlation ID (also PaymentTransactionId)")
            .ValueGeneratedNever();

        // State
        entity.Property(x => x.CurrentState)
            .HasComment("Current state of the saga state machine")
            .HasMaxLength(64)
            .IsRequired();

        entity.HasIndex(x => x.CurrentState)
            .HasDatabaseName("IX_SubscriptionExtensionSagaState_CurrentState");

        // Business properties
        entity.Property(x => x.UserId)
            .HasComment("User who is extending the subscription");

        entity.HasIndex(x => x.UserId)
            .HasDatabaseName("IX_SubscriptionExtensionSagaState_UserId");

        entity.Property(x => x.PaymentMethodId)
            .HasComment("ID of the saved payment method");

        entity.Property(x => x.DurationDays)
            .HasComment("Subscription extension duration in days");

        entity.Property(x => x.Amount)
            .HasComment("Payment amount")
            .HasPrecision(19, 4);

        entity.Property(x => x.Currency)
            .HasComment("ISO 4217 currency code")
            .HasMaxLength(3)
            .IsRequired();

        entity.Property(x => x.IdempotencyKey)
            .HasComment("Idempotency key to prevent duplicate extensions")
            .HasMaxLength(128)
            .IsRequired();

        entity.HasIndex(x => x.IdempotencyKey)
            .HasDatabaseName("IX_SubscriptionExtensionSagaState_IdempotencyKey")
            .IsUnique();

        entity.Property(x => x.PaymentTransactionId)
            .HasComment("Payment transaction ID (set after payment completes)");

        // Timestamps
        entity.Property(x => x.ExtensionInitiatedAtUtc)
            .HasComment("UTC timestamp when extension was initiated");

        entity.Property(x => x.CreatedUtc)
            .HasComment("UTC timestamp when saga was created");

        entity.Property(x => x.LastModifiedUtc)
            .HasComment("UTC timestamp when saga was last updated");

        entity.Property(x => x.PaymentCompletedAtUtc)
            .HasComment("UTC timestamp when payment completed (null if not completed)");

        entity.Property(x => x.ExtensionCompletedAtUtc)
            .HasComment("UTC timestamp when extension completed (null if not completed)");

        entity.Property(x => x.NewExpiresAtUtc)
            .HasComment("New subscription expiration date after extension (null if not completed)");

        // Error handling
        entity.Property(x => x.ErrorCode)
            .HasComment("Error code for categorized failure handling")
            .HasMaxLength(64);

        entity.Property(x => x.ErrorMessage)
            .HasComment("Error message if failed")
            .HasMaxLength(2048);

        // Compensation
        entity.Property(x => x.CompensationTriggered)
            .HasComment("Whether compensation (refund) has been triggered");

        entity.Property(x => x.CompensationCompletedAtUtc)
            .HasComment("UTC timestamp when compensation completed");

        // Scheduler tokens
        entity.Property(x => x.PaymentTimeoutTokenId)
            .HasComment("Token ID for payment timeout scheduler - set when schedule is active");

        entity.Property(x => x.ExtensionTimeoutTokenId)
            .HasComment("Token ID for extension timeout scheduler - set when schedule is active");

        entity.Property(x => x.CompensationTimeoutTokenId)
            .HasComment("Token ID for compensation timeout scheduler - set when schedule is active");

        entity.Property(s => s.RowVersion)
            .IsRowVersion()
            .HasComment("Optimistic concurrency token.");

        entity.HasIndex(x => new
        {
            x.CurrentState,
            x.CreatedUtc
        })
            .HasDatabaseName("IX_SubscriptionExtensionSagaState_State_Created");

        // Index for stuck saga health check queries
        entity.HasIndex(x => new
        {
            x.CurrentState,
            LastUpdatedAtUtc = x.LastModifiedUtc
        })
            .HasDatabaseName("IX_SubscriptionExtensionSagaState_State_LastUpdated");
    }
}
