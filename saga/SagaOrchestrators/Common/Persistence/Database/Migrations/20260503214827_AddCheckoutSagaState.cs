using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SagaOrchestrators.Common.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutSagaState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckoutSagaState",
                schema: "saga",
                columns: table => new
                {
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Workflow correlation id - equals BasketCheckoutInitiatedEvent.BasketCorrelationId (ADR-0008)."),
                    current_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Current state of the saga state machine."),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "User initiating checkout. Becomes Ordering's BuyerId."),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false, comment: "Sum of basket line totals captured at checkout initiation."),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, comment: "ISO 4217 currency code."),
                    payment_method_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Saved payment method id - passed through to PaymentProcessingSaga."),
                    basket_snapshot_json = table.Column<string>(type: "jsonb", nullable: false, comment: "Serialized basket line snapshot (immutable for the saga's lifetime)."),
                    shipping_address_json = table.Column<string>(type: "jsonb", nullable: true, comment: "Serialized shipping Address value object. Nulled out on terminal per ADR-0011."),
                    billing_address_json = table.Column<string>(type: "jsonb", nullable: true, comment: "Serialized billing Address value object. Nulled out on terminal per ADR-0011."),
                    initiated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the saga was initiated (copied from the Basket event)."),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Ordering aggregate id assigned after OrderCreatedEvent. Null until OrderCreated arrives."),
                    order_created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when Ordering reported the order created."),
                    reservation_ids_json = table.Column<string>(type: "jsonb", nullable: false, comment: "Serialized per-ProductId reservation tracking dictionary."),
                    expected_reservations = table.Column<int>(type: "integer", nullable: false, defaultValue: 0, comment: "Number of distinct ProductIds in the basket - target reservation count."),
                    pending_reservations = table.Column<int>(type: "integer", nullable: false, defaultValue: 0, comment: "Decremented on each StockReservedSagaEvent. Zero triggers AwaitingPayment."),
                    stock_reservation_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when stock reservation fan-out began."),
                    stock_reservation_completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when all reservations completed."),
                    payment_transaction_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Payment transaction id from PaymentProcessingSaga. Required for refund compensation."),
                    payment_requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when PaymentRequestedEvent was emitted."),
                    payment_completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when PaymentCompletedSagaEvent was received."),
                    order_confirmation_requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when ConfirmOrderCommand was dispatched."),
                    order_confirmed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when OrderConfirmedSagaEvent arrived."),
                    pending_releases = table.Column<int>(type: "integer", nullable: false, defaultValue: 0, comment: "Decremented on each ReservationReleasedSagaEvent during compensation. Zero AND OrderCancelledReceived=true gates the transition to Compensated."),
                    order_cancelled_received = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false, comment: "True once OrderCancelledSagaEvent has been observed during compensation - gates the transition to Compensated."),
                    compensation_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp at first transition into any Compensating* state."),
                    compensation_completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp at transition into Compensated."),
                    compensation_triggered = table.Column<bool>(type: "boolean", nullable: false, comment: "Set true on the first Compensating* transition."),
                    error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, comment: "Categorised failure code (e.g., STOCK_UNAVAILABLE, PAYMENT_FAILED)."),
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

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSagaState_CurrentState",
                schema: "saga",
                table: "CheckoutSagaState",
                column: "current_state");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSagaState_OrderId",
                schema: "saga",
                table: "CheckoutSagaState",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSagaState_State_Created",
                schema: "saga",
                table: "CheckoutSagaState",
                columns: new[] { "current_state", "created_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSagaState_State_LastUpdated",
                schema: "saga",
                table: "CheckoutSagaState",
                columns: new[] { "current_state", "last_modified_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSagaState_UserId",
                schema: "saga",
                table: "CheckoutSagaState",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckoutSagaState",
                schema: "saga");
        }
    }
}
