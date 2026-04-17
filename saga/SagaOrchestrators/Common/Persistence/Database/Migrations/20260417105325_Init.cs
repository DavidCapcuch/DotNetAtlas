using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SagaOrchestrators.Common.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
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
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "PK - Unique correlation ID (also PaymentTransactionId)"),
                    current_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Current state of the saga state machine"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "User who is extending the subscription"),
                    payment_method_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "ID of the saved payment method"),
                    duration_days = table.Column<int>(type: "integer", nullable: false, comment: "Subscription extension duration in days"),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false, comment: "Payment amount"),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, comment: "ISO 4217 currency code"),
                    payment_transaction_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Payment transaction ID (set after payment completes)"),
                    extension_initiated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when extension was initiated"),
                    payment_completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when payment completed (null if not completed)"),
                    created_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when saga was created"),
                    last_modified_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when saga was last updated"),
                    extension_completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when extension completed (null if not completed)"),
                    new_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "New subscription expiration date after extension (null if not completed)"),
                    error_message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true, comment: "Error message if failed"),
                    error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, comment: "Error code for categorized failure handling"),
                    compensation_triggered = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether compensation (refund) has been triggered"),
                    compensation_completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when compensation completed"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false, comment: "Optimistic concurrency token."),
                    payment_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Token ID for payment timeout scheduler - set when schedule is active"),
                    extension_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Token ID for extension timeout scheduler - set when schedule is active"),
                    compensation_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Token ID for compensation timeout scheduler - set when schedule is active")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alert_subscription_extension_saga_state", x => x.correlation_id);
                },
                comment: "Saga state for alert subscription extension orchestration.");

            migrationBuilder.CreateTable(
                name: "AlertSubscriptionPurchaseSagaState",
                schema: "saga",
                columns: table => new
                {
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "PK - Unique correlation ID (also PaymentTransactionId)"),
                    current_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Current state of the saga state machine"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "User who purchased the subscription"),
                    payment_method_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "ID of the saved payment method"),
                    subscription_tier = table.Column<int>(type: "integer", nullable: false, comment: "Subscription tier (Pro, Ultra)"),
                    duration_days = table.Column<int>(type: "integer", nullable: false, comment: "Subscription duration in days"),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false, comment: "Payment amount"),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, comment: "ISO 4217 currency code"),
                    payment_transaction_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Payment transaction ID (set after payment completes)"),
                    purchase_initiated_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when purchase was initiated"),
                    payment_completed_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when payment completed (null if not completed)"),
                    created_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when saga was created"),
                    last_modified_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when saga was last updated"),
                    activation_completed_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when activation completed (null if not completed)"),
                    error_message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true, comment: "Error message if failed"),
                    error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, comment: "Error code for categorized failure handling"),
                    compensation_triggered = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether compensation (refund) has been triggered"),
                    compensation_completed_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when compensation completed"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false, comment: "Optimistic concurrency token."),
                    payment_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Token ID for payment timeout scheduler - set when schedule is active"),
                    activation_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Token ID for activation timeout scheduler - set when schedule is active"),
                    compensation_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Token ID for compensation timeout scheduler - set when schedule is active")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alert_subscription_purchase_saga_state", x => x.correlation_id);
                },
                comment: "Saga state for alert subscription purchase orchestration.");

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "saga",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, comment: "PK, Identity")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    topic_name = table.Column<string>(type: "character varying(249)", unicode: false, maxLength: 249, nullable: false, comment: "The Kafka topic where this message will be published. Set by the message producer."),
                    kafka_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, comment: "Kafka Key - typically the Aggregate ID for proper event ordering and partitioning"),
                    avro_payload = table.Column<byte[]>(type: "bytea", nullable: false, comment: "Avro-serialized domain event payload"),
                    type = table.Column<string>(type: "character varying(255)", unicode: false, maxLength: 255, nullable: false, comment: "Avro type name of the serialized event (e.g., 'FeedbackChangedEvent') for deserialization and observability"),
                    headers = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true, comment: "JSON dictionary of OpenTelemetry-standard headers for distributed tracing and metadata. Headers are automatically generated by OpenTelemetry propagators for end-to-end trace context propagation."),
                    created_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Creation timestamp (UTC).")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                },
                comment: "Outbox pattern table for storing domain events as Avro-serialized messages for reliable event publishing.");

            migrationBuilder.CreateTable(
                name: "PaymentProcessingSagaState",
                schema: "saga",
                columns: table => new
                {
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Unique correlation ID for the payment saga"),
                    current_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Current state of the saga state machine"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "User initiating the payment"),
                    payment_method_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "ID of the saved payment method"),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false, comment: "Payment amount"),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, comment: "ISO 4217 currency code"),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Idempotency key to prevent duplicate processing"),
                    authorization_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "Authorization ID from payment provider"),
                    authorization_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when authorization expires"),
                    payment_transaction_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Payment transaction ID after capture"),
                    initiated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when payment was initiated"),
                    created_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when saga was created"),
                    last_modified_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when saga was last updated"),
                    authorized_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when authorization completed"),
                    captured_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when capture completed"),
                    authorization_retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0, comment: "Number of authorization retry attempts"),
                    capture_retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0, comment: "Number of capture retry attempts"),
                    error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, comment: "Error code for categorized failure handling"),
                    error_message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true, comment: "Error message if failed"),
                    compensation_triggered = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether compensation has been triggered"),
                    compensation_completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when compensation completed"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false, comment: "Optimistic concurrency token."),
                    authorization_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Token ID for authorization timeout scheduler - set when schedule is active"),
                    capture_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Token ID for capture timeout scheduler - set when schedule is active"),
                    void_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Token ID for void timeout scheduler - set when schedule is active"),
                    refund_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Token ID for refund timeout scheduler - set when schedule is active"),
                    success_finalization_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Token ID for success finalization timeout scheduler - set when schedule is active")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_processing_saga_state", x => x.correlation_id);
                },
                comment: "Saga state for payment processing orchestration.");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionExtensionSagaState_CurrentState",
                schema: "saga",
                table: "AlertSubscriptionExtensionSagaState",
                column: "current_state");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionExtensionSagaState_State_Created",
                schema: "saga",
                table: "AlertSubscriptionExtensionSagaState",
                columns: new[] { "current_state", "created_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionExtensionSagaState_State_LastUpdated",
                schema: "saga",
                table: "AlertSubscriptionExtensionSagaState",
                columns: new[] { "current_state", "last_modified_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionExtensionSagaState_UserId",
                schema: "saga",
                table: "AlertSubscriptionExtensionSagaState",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPurchaseSagaState_CurrentState",
                schema: "saga",
                table: "AlertSubscriptionPurchaseSagaState",
                column: "current_state");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPurchaseSagaState_State_Created",
                schema: "saga",
                table: "AlertSubscriptionPurchaseSagaState",
                columns: new[] { "current_state", "created_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPurchaseSagaState_State_LastUpdated",
                schema: "saga",
                table: "AlertSubscriptionPurchaseSagaState",
                columns: new[] { "current_state", "last_modified_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPurchaseSagaState_UserId",
                schema: "saga",
                table: "AlertSubscriptionPurchaseSagaState",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSagaState_CurrentState",
                schema: "saga",
                table: "PaymentProcessingSagaState",
                column: "current_state");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSagaState_IdempotencyKey",
                schema: "saga",
                table: "PaymentProcessingSagaState",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSagaState_State_Created",
                schema: "saga",
                table: "PaymentProcessingSagaState",
                columns: new[] { "current_state", "created_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSagaState_State_LastUpdated",
                schema: "saga",
                table: "PaymentProcessingSagaState",
                columns: new[] { "current_state", "last_modified_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSagaState_UserId",
                schema: "saga",
                table: "PaymentProcessingSagaState",
                column: "user_id");
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
