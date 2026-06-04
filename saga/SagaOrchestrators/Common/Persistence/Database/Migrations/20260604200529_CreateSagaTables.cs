using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SagaOrchestrators.Common.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class CreateSagaTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "saga");

            migrationBuilder.CreateTable(
                name: "checkout_saga_state",
                schema: "saga",
                columns: table => new
                {
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "MassTransit saga instance id (ISaga.CorrelationId); equals the pre-assigned OrderId (ADR-0029)."),
                    current_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Current state of the saga state machine."),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "User initiating checkout. Becomes Ordering's BuyerId."),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false, comment: "Sum of basket line totals captured at checkout initiation."),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, comment: "ISO 4217 currency code."),
                    payment_method_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Saved payment method id (Guid). Stored as uuid because Basket + Ordering wire shapes still use Guid; CheckoutSaga string-encodes it only at the Payments-emit boundary (C-2 closeout — Payments-side schema changed, upstream BC wire shapes deferred)."),
                    basket_snapshot_json = table.Column<string>(type: "jsonb", nullable: false, comment: "Serialized basket line snapshot (immutable for the saga's lifetime)."),
                    shipping_address_json = table.Column<string>(type: "jsonb", nullable: true, comment: "Serialized shipping Address value object. Nulled out on terminal per ADR-0011."),
                    billing_address_json = table.Column<string>(type: "jsonb", nullable: true, comment: "Serialized billing Address value object. Nulled out on terminal per ADR-0011."),
                    initiated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the saga was initiated (copied from the Basket event)."),
                    order_created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when Ordering reported the order created."),
                    reservation_ids_json = table.Column<string>(type: "jsonb", nullable: false, comment: "Serialized per-ProductId reservation tracking dictionary."),
                    expected_reservations = table.Column<int>(type: "integer", nullable: false, defaultValue: 0, comment: "Number of distinct ProductIds in the basket - target reservation count."),
                    pending_reservations = table.Column<int>(type: "integer", nullable: false, defaultValue: 0, comment: "Decremented on each StockReservedSagaEvent. Zero triggers AwaitingPayment."),
                    stock_reservation_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when stock reservation fan-out began."),
                    stock_reservation_completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when all reservations completed."),
                    payment_transaction_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Payment transaction id from PaymentProcessingSaga. Required for refund compensation."),
                    payment_requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when RequestPaymentCommand was emitted to payments.payment-commands (per ADR-0023; renamed from PaymentRequestedEvent)."),
                    payment_completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when PaymentCompletedSagaEvent was received."),
                    order_confirmation_requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when ConfirmOrderCommand was dispatched."),
                    order_confirmed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when OrderConfirmedSagaEvent arrived."),
                    pending_releases = table.Column<int>(type: "integer", nullable: false, defaultValue: 0, comment: "Decremented on each ReservationReleasedSagaEvent during compensation. Zero AND OrderCancelledReceived=true gates the transition to Compensated."),
                    order_cancelled_received = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false, comment: "True once OrderCancelledSagaEvent has been observed during compensation - gates the transition to Compensated."),
                    compensation_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp at first transition into any Compensating* state."),
                    compensation_completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp at transition into Compensated."),
                    compensation_triggered = table.Column<bool>(type: "boolean", nullable: false, comment: "Set true on the first Compensating* transition."),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Categorised failure code (e.g., STOCK_UNAVAILABLE, PAYMENT_FAILED)."),
                    error_message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true, comment: "Human-readable failure message."),
                    failed_at_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, comment: "Name of the state when failure first occurred. Aids ops forensics."),
                    order_creation_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Token ID for the order-creation timeout scheduler - set when schedule is active."),
                    stock_reservation_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Token ID for the stock-reservation timeout scheduler - set when schedule is active."),
                    payment_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Token ID for the payment timeout scheduler - set when schedule is active."),
                    order_confirmation_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Token ID for the order-confirmation timeout scheduler - set when schedule is active."),
                    compensation_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Token ID for the compensation timeout scheduler - set when schedule is active."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false, comment: "Optimistic concurrency token."),
                    created_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when saga row was created."),
                    last_modified_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when saga row was last mutated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checkout_saga_state", x => x.correlation_id);
                },
                comment: "Saga state for the checkout orchestration.");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
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
                name: "payment_processing_saga_state",
                schema: "saga",
                columns: table => new
                {
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "MassTransit saga instance id (ISaga.CorrelationId); equals the pre-assigned OrderId (ADR-0029)."),
                    current_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Current state of the saga state machine"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "User initiating the payment"),
                    payment_method_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Gateway-issued opaque payment-method token (Stripe 'pm_*', Adyen alphanumeric, …); 1-64 chars. Changed from uuid in the Wave-1 closeout C-2 fix."),
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
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Error code for categorized failure handling"),
                    error_message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true, comment: "Error message if failed"),
                    compensation_triggered = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether compensation has been triggered"),
                    compensation_completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when compensation completed"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false, comment: "Optimistic concurrency token."),
                    authorization_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Token ID for authorization timeout scheduler - set when schedule is active"),
                    capture_approval_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Token ID for capture-approval wait-state timeout scheduler - set when schedule is active"),
                    capture_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Token ID for capture timeout scheduler - set when schedule is active"),
                    void_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Token ID for void timeout scheduler - set when schedule is active")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_processing_saga_state", x => x.correlation_id);
                },
                comment: "Saga state for payment processing orchestration.");

            migrationBuilder.CreateIndex(
                name: "ix_checkout_saga_state_current_state",
                schema: "saga",
                table: "checkout_saga_state",
                column: "current_state");

            migrationBuilder.CreateIndex(
                name: "ix_checkout_saga_state_state_created",
                schema: "saga",
                table: "checkout_saga_state",
                columns: new[] { "current_state", "created_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_checkout_saga_state_state_last_updated",
                schema: "saga",
                table: "checkout_saga_state",
                columns: new[] { "current_state", "last_modified_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_checkout_saga_state_user_id",
                schema: "saga",
                table: "checkout_saga_state",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_processing_saga_state_current_state",
                schema: "saga",
                table: "payment_processing_saga_state",
                column: "current_state");

            migrationBuilder.CreateIndex(
                name: "ix_payment_processing_saga_state_state_created",
                schema: "saga",
                table: "payment_processing_saga_state",
                columns: new[] { "current_state", "created_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_processing_saga_state_state_last_updated",
                schema: "saga",
                table: "payment_processing_saga_state",
                columns: new[] { "current_state", "last_modified_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_processing_saga_state_user_id",
                schema: "saga",
                table: "payment_processing_saga_state",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_payment_processing_saga_state_idempotency_key",
                schema: "saga",
                table: "payment_processing_saga_state",
                column: "idempotency_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "checkout_saga_state",
                schema: "saga");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "saga");

            migrationBuilder.DropTable(
                name: "payment_processing_saga_state",
                schema: "saga");
        }
    }
}
