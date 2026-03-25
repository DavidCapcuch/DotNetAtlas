using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SagaOrchestrators.Common.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class Asdf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "saga");

            migrationBuilder.CreateTable(
                name: "AlertSubscriptionExtensionSagaState",
                schema: "saga",
                columns: table => new
                {
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "PK - Unique correlation ID (also PaymentTransactionId)"),
                    CurrentState = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, comment: "Current state of the saga state machine"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "User who is extending the subscription"),
                    PaymentMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "ID of the saved payment method"),
                    DurationDays = table.Column<int>(type: "int", nullable: false, comment: "Subscription extension duration in days"),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false, comment: "Payment amount"),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, comment: "ISO 4217 currency code"),
                    PaymentTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true, comment: "Payment transaction ID (set after payment completes)"),
                    ExtensionInitiatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, comment: "UTC timestamp when extension was initiated"),
                    PaymentCompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true, comment: "UTC timestamp when payment completed (null if not completed)"),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, comment: "UTC timestamp when saga was created"),
                    LastModifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, comment: "UTC timestamp when saga was last updated"),
                    ExtensionCompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true, comment: "UTC timestamp when extension completed (null if not completed)"),
                    NewExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true, comment: "New subscription expiration date after extension (null if not completed)"),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true, comment: "Error message if failed"),
                    ErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, comment: "Error code for categorized failure handling"),
                    CompensationTriggered = table.Column<bool>(type: "bit", nullable: false, comment: "Whether compensation (refund) has been triggered"),
                    CompensationCompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true, comment: "UTC timestamp when compensation completed"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true, comment: "Optimistic concurrency token."),
                    PaymentTimeoutTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true, comment: "Token ID for payment timeout scheduler - set when schedule is active"),
                    ExtensionTimeoutTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true, comment: "Token ID for extension timeout scheduler - set when schedule is active"),
                    CompensationTimeoutTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true, comment: "Token ID for compensation timeout scheduler - set when schedule is active")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertSubscriptionExtensionSagaState", x => x.CorrelationId);
                },
                comment: "Saga state for alert subscription extension orchestration.");

            migrationBuilder.CreateTable(
                name: "AlertSubscriptionPurchaseSagaState",
                schema: "saga",
                columns: table => new
                {
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "PK - Unique correlation ID (also PaymentTransactionId)"),
                    CurrentState = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, comment: "Current state of the saga state machine"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "User who purchased the subscription"),
                    PaymentMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "ID of the saved payment method"),
                    SubscriptionTier = table.Column<int>(type: "int", nullable: false, comment: "Subscription tier (Pro, Ultra)"),
                    DurationDays = table.Column<int>(type: "int", nullable: false, comment: "Subscription duration in days"),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false, comment: "Payment amount"),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, comment: "ISO 4217 currency code"),
                    PaymentTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true, comment: "Payment transaction ID (set after payment completes)"),
                    PurchaseInitiatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, comment: "UTC timestamp when purchase was initiated"),
                    PaymentCompletedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true, comment: "UTC timestamp when payment completed (null if not completed)"),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, comment: "UTC timestamp when saga was created"),
                    LastModifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, comment: "UTC timestamp when saga was last updated"),
                    ActivationCompletedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true, comment: "UTC timestamp when activation completed (null if not completed)"),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true, comment: "Error message if failed"),
                    ErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, comment: "Error code for categorized failure handling"),
                    CompensationTriggered = table.Column<bool>(type: "bit", nullable: false, comment: "Whether compensation (refund) has been triggered"),
                    CompensationCompletedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true, comment: "UTC timestamp when compensation completed"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true, comment: "Optimistic concurrency token."),
                    PaymentTimeoutTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true, comment: "Token ID for payment timeout scheduler - set when schedule is active"),
                    ActivationTimeoutTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true, comment: "Token ID for activation timeout scheduler - set when schedule is active"),
                    CompensationTimeoutTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true, comment: "Token ID for compensation timeout scheduler - set when schedule is active")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertSubscriptionPurchaseSagaState", x => x.CorrelationId);
                },
                comment: "Saga state for alert subscription purchase orchestration.");

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "saga",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false, comment: "PK, Identity")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TopicName = table.Column<string>(type: "varchar(249)", unicode: false, maxLength: 249, nullable: false, comment: "The Kafka topic where this message will be published. Set by the message producer."),
                    KafkaKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, comment: "Kafka Key - typically the Aggregate ID for proper event ordering and partitioning"),
                    AvroPayload = table.Column<byte[]>(type: "varbinary(max)", nullable: false, comment: "Avro-serialized domain event payload"),
                    Type = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false, comment: "Avro type name of the serialized event (e.g., 'FeedbackChangedEvent') for deserialization and observability"),
                    Headers = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true, comment: "JSON dictionary of OpenTelemetry-standard headers for distributed tracing and metadata. Headers are automatically generated by OpenTelemetry propagators for end-to-end trace context propagation."),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, comment: "Creation timestamp (UTC).")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                },
                comment: "Outbox pattern table for storing domain events as Avro-serialized messages for reliable event publishing.");

            migrationBuilder.CreateTable(
                name: "PaymentProcessingSagaState",
                schema: "saga",
                columns: table => new
                {
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "Unique correlation ID for the payment saga"),
                    CurrentState = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, comment: "Current state of the saga state machine"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "User initiating the payment"),
                    PaymentMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "ID of the saved payment method"),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false, comment: "Payment amount"),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, comment: "ISO 4217 currency code"),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, comment: "Idempotency key to prevent duplicate processing"),
                    AuthorizationId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true, comment: "Authorization ID from payment provider"),
                    AuthorizationExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true, comment: "UTC timestamp when authorization expires"),
                    PaymentTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true, comment: "Payment transaction ID after capture"),
                    InitiatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, comment: "UTC timestamp when payment was initiated"),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, comment: "UTC timestamp when saga was created"),
                    LastModifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, comment: "UTC timestamp when saga was last updated"),
                    AuthorizedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true, comment: "UTC timestamp when authorization completed"),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true, comment: "UTC timestamp when capture completed"),
                    AuthorizationRetryCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0, comment: "Number of authorization retry attempts"),
                    CaptureRetryCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0, comment: "Number of capture retry attempts"),
                    ErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, comment: "Error code for categorized failure handling"),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true, comment: "Error message if failed"),
                    CompensationTriggered = table.Column<bool>(type: "bit", nullable: false, comment: "Whether compensation has been triggered"),
                    CompensationCompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true, comment: "UTC timestamp when compensation completed"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true, comment: "Optimistic concurrency token."),
                    AuthorizationTimeoutTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true, comment: "Token ID for authorization timeout scheduler - set when schedule is active"),
                    CaptureTimeoutTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true, comment: "Token ID for capture timeout scheduler - set when schedule is active"),
                    VoidTimeoutTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true, comment: "Token ID for void timeout scheduler - set when schedule is active"),
                    RefundTimeoutTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true, comment: "Token ID for refund timeout scheduler - set when schedule is active"),
                    SuccessFinalizationTimeoutTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true, comment: "Token ID for success finalization timeout scheduler - set when schedule is active")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentProcessingSagaState", x => x.CorrelationId);
                },
                comment: "Saga state for payment processing orchestration.");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionExtensionSagaState_CurrentState",
                schema: "saga",
                table: "AlertSubscriptionExtensionSagaState",
                column: "CurrentState");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionExtensionSagaState_State_Created",
                schema: "saga",
                table: "AlertSubscriptionExtensionSagaState",
                columns: new[] { "CurrentState", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionExtensionSagaState_State_LastUpdated",
                schema: "saga",
                table: "AlertSubscriptionExtensionSagaState",
                columns: new[] { "CurrentState", "LastModifiedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionExtensionSagaState_UserId",
                schema: "saga",
                table: "AlertSubscriptionExtensionSagaState",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPurchaseSagaState_CurrentState",
                schema: "saga",
                table: "AlertSubscriptionPurchaseSagaState",
                column: "CurrentState");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPurchaseSagaState_State_Created",
                schema: "saga",
                table: "AlertSubscriptionPurchaseSagaState",
                columns: new[] { "CurrentState", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPurchaseSagaState_State_LastUpdated",
                schema: "saga",
                table: "AlertSubscriptionPurchaseSagaState",
                columns: new[] { "CurrentState", "LastModifiedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPurchaseSagaState_UserId",
                schema: "saga",
                table: "AlertSubscriptionPurchaseSagaState",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSagaState_CurrentState",
                schema: "saga",
                table: "PaymentProcessingSagaState",
                column: "CurrentState");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSagaState_IdempotencyKey",
                schema: "saga",
                table: "PaymentProcessingSagaState",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSagaState_State_Created",
                schema: "saga",
                table: "PaymentProcessingSagaState",
                columns: new[] { "CurrentState", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSagaState_State_LastUpdated",
                schema: "saga",
                table: "PaymentProcessingSagaState",
                columns: new[] { "CurrentState", "LastModifiedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSagaState_UserId",
                schema: "saga",
                table: "PaymentProcessingSagaState",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertSubscriptionExtensionSagaState",
                schema: "saga");

            migrationBuilder.DropTable(
                name: "AlertSubscriptionPurchaseSagaState",
                schema: "saga");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "saga");

            migrationBuilder.DropTable(
                name: "PaymentProcessingSagaState",
                schema: "saga");
        }
    }
}
