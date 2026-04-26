using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invoicing.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingProjectionsAndInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "invoicing",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Unique message identifier (Primary Key)."),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the message was processed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => x.message_id);
                },
                comment: "Inbox pattern table for idempotent message processing. Tracks processed messages to prevent duplicate processing.");

            migrationBuilder.CreateTable(
                name: "pending_credit_notes",
                schema: "invoicing",
                columns: table => new
                {
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Saga / cross-BC correlation id. Primary key."),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "OrderCancelledEvent.OrderId; null until the order-cancel half arrives."),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "PaymentRefundedEvent.PaymentTransactionId — the original captured payment, not the refund txn id."),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "OrderCancelledEvent.BuyerId; M7's outbox publisher uses this as the partition key."),
                    order_payload = table.Column<string>(type: "jsonb", nullable: true, comment: "PII: full OrderCancelledEvent serialised to JSON for M7 hydration."),
                    payment_payload = table.Column<string>(type: "jsonb", nullable: true, comment: "Full PaymentRefundedEvent serialised to JSON for M7 hydration."),
                    first_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Wall-clock at first observation; never overwritten."),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Set when both halves are present."),
                    issued_credit_note_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Set by M7's IssueCreditNoteCommandHandler atomically with the CreditNote aggregate insert.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_credit_notes", x => x.correlation_id);
                },
                comment: "Async-enrichment buffer: collects OrderCancelledEvent + PaymentRefundedEvent halves keyed on CorrelationId until M7's IssueCreditNoteCommandHandler converts the converged row into a CreditNote aggregate.");

            migrationBuilder.CreateTable(
                name: "pending_invoices",
                schema: "invoicing",
                columns: table => new
                {
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Saga / cross-BC correlation id. Primary key."),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "OrderConfirmedEvent.OrderId; null until the order half arrives."),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "PaymentCapturedEvent.PaymentTransactionId; null until the payment half arrives."),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "OrderConfirmedEvent.BuyerId; M7's outbox publisher uses this as the partition key on invoicing.invoices."),
                    order_payload = table.Column<string>(type: "jsonb", nullable: true, comment: "PII: full OrderConfirmedEvent serialised to JSON for M7 hydration."),
                    payment_payload = table.Column<string>(type: "jsonb", nullable: true, comment: "PII: full PaymentCapturedEvent serialised to JSON for M7 hydration."),
                    first_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Wall-clock at first observation; never overwritten on subsequent updates."),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Set when both halves are present."),
                    issued_invoice_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Set by M7's IssueInvoiceCommandHandler atomically with the Invoice aggregate insert.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_invoices", x => x.correlation_id);
                },
                comment: "Async-enrichment buffer: collects OrderConfirmedEvent + PaymentCapturedEvent halves keyed on CorrelationId until M7's IssueInvoiceCommandHandler converts the converged row into an Invoice aggregate.");

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_ProcessedAtUtc",
                schema: "invoicing",
                table: "InboxMessages",
                column: "processed_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_pending_credit_notes_order_id",
                schema: "invoicing",
                table: "pending_credit_notes",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_pending_credit_notes_ready",
                schema: "invoicing",
                table: "pending_credit_notes",
                columns: new[] { "completed_at_utc", "issued_credit_note_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pending_invoices_order_id",
                schema: "invoicing",
                table: "pending_invoices",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_pending_invoices_ready",
                schema: "invoicing",
                table: "pending_invoices",
                columns: new[] { "completed_at_utc", "issued_invoice_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "pending_credit_notes",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "pending_invoices",
                schema: "invoicing");
        }
    }
}
