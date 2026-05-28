using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invoicing.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class TrimColumnCommentNoise : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "pending_invoices",
                schema: "invoicing",
                comment: "Async-enrichment buffer: collects OrderConfirmedEvent + PaymentCapturedEvent halves keyed on CorrelationId until IssueInvoiceCommandHandler converts the converged row into an Invoice aggregate.",
                oldComment: "Async-enrichment buffer: collects OrderConfirmedEvent + PaymentCapturedEvent halves keyed on CorrelationId until M7's IssueInvoiceCommandHandler converts the converged row into an Invoice aggregate.");

            migrationBuilder.AlterTable(
                name: "pending_credit_notes",
                schema: "invoicing",
                comment: "Async-enrichment buffer: collects OrderCancelledEvent + PaymentRefundedEvent halves keyed on CorrelationId until IssueCreditNoteCommandHandler converts the converged row into a CreditNote aggregate.",
                oldComment: "Async-enrichment buffer: collects OrderCancelledEvent + PaymentRefundedEvent halves keyed on CorrelationId until M7's IssueCreditNoteCommandHandler converts the converged row into a CreditNote aggregate.");

            migrationBuilder.AlterColumn<string>(
                name: "payment_payload",
                schema: "invoicing",
                table: "pending_invoices",
                type: "jsonb",
                nullable: true,
                comment: "PII: full PaymentCapturedEvent serialised to JSON for issuance-time hydration.",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true,
                oldComment: "PII: full PaymentCapturedEvent serialised to JSON for M7 hydration.");

            migrationBuilder.AlterColumn<string>(
                name: "order_payload",
                schema: "invoicing",
                table: "pending_invoices",
                type: "jsonb",
                nullable: true,
                comment: "PII: full OrderConfirmedEvent serialised to JSON for issuance-time hydration.",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true,
                oldComment: "PII: full OrderConfirmedEvent serialised to JSON for M7 hydration.");

            migrationBuilder.AlterColumn<Guid>(
                name: "issued_invoice_id",
                schema: "invoicing",
                table: "pending_invoices",
                type: "uuid",
                nullable: true,
                comment: "Set by IssueInvoiceCommandHandler atomically with the Invoice aggregate insert.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Set by M7's IssueInvoiceCommandHandler atomically with the Invoice aggregate insert.");

            migrationBuilder.AlterColumn<Guid>(
                name: "buyer_id",
                schema: "invoicing",
                table: "pending_invoices",
                type: "uuid",
                nullable: true,
                comment: "OrderConfirmedEvent.BuyerId; the outbox publisher uses this as the partition key on invoicing.invoices.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "OrderConfirmedEvent.BuyerId; M7's outbox publisher uses this as the partition key on invoicing.invoices.");

            migrationBuilder.AlterColumn<string>(
                name: "payment_payload",
                schema: "invoicing",
                table: "pending_credit_notes",
                type: "jsonb",
                nullable: true,
                comment: "Full PaymentRefundedEvent serialised to JSON for issuance-time hydration.",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true,
                oldComment: "Full PaymentRefundedEvent serialised to JSON for M7 hydration.");

            migrationBuilder.AlterColumn<string>(
                name: "order_payload",
                schema: "invoicing",
                table: "pending_credit_notes",
                type: "jsonb",
                nullable: true,
                comment: "PII: full OrderCancelledEvent serialised to JSON for issuance-time hydration.",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true,
                oldComment: "PII: full OrderCancelledEvent serialised to JSON for M7 hydration.");

            migrationBuilder.AlterColumn<Guid>(
                name: "issued_credit_note_id",
                schema: "invoicing",
                table: "pending_credit_notes",
                type: "uuid",
                nullable: true,
                comment: "Set by IssueCreditNoteCommandHandler atomically with the CreditNote aggregate insert.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Set by M7's IssueCreditNoteCommandHandler atomically with the CreditNote aggregate insert.");

            migrationBuilder.AlterColumn<Guid>(
                name: "buyer_id",
                schema: "invoicing",
                table: "pending_credit_notes",
                type: "uuid",
                nullable: true,
                comment: "OrderCancelledEvent.BuyerId; the outbox publisher uses this as the partition key.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "OrderCancelledEvent.BuyerId; M7's outbox publisher uses this as the partition key.");

            migrationBuilder.AlterColumn<Guid>(
                name: "correlation_id",
                schema: "invoicing",
                table: "credit_notes",
                type: "uuid",
                nullable: false,
                comment: "Cancellation flow correlation id; used as idempotency key.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Cancellation flow correlation id; used as M7 idempotency key.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "pending_invoices",
                schema: "invoicing",
                comment: "Async-enrichment buffer: collects OrderConfirmedEvent + PaymentCapturedEvent halves keyed on CorrelationId until M7's IssueInvoiceCommandHandler converts the converged row into an Invoice aggregate.",
                oldComment: "Async-enrichment buffer: collects OrderConfirmedEvent + PaymentCapturedEvent halves keyed on CorrelationId until IssueInvoiceCommandHandler converts the converged row into an Invoice aggregate.");

            migrationBuilder.AlterTable(
                name: "pending_credit_notes",
                schema: "invoicing",
                comment: "Async-enrichment buffer: collects OrderCancelledEvent + PaymentRefundedEvent halves keyed on CorrelationId until M7's IssueCreditNoteCommandHandler converts the converged row into a CreditNote aggregate.",
                oldComment: "Async-enrichment buffer: collects OrderCancelledEvent + PaymentRefundedEvent halves keyed on CorrelationId until IssueCreditNoteCommandHandler converts the converged row into a CreditNote aggregate.");

            migrationBuilder.AlterColumn<string>(
                name: "payment_payload",
                schema: "invoicing",
                table: "pending_invoices",
                type: "jsonb",
                nullable: true,
                comment: "PII: full PaymentCapturedEvent serialised to JSON for M7 hydration.",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true,
                oldComment: "PII: full PaymentCapturedEvent serialised to JSON for issuance-time hydration.");

            migrationBuilder.AlterColumn<string>(
                name: "order_payload",
                schema: "invoicing",
                table: "pending_invoices",
                type: "jsonb",
                nullable: true,
                comment: "PII: full OrderConfirmedEvent serialised to JSON for M7 hydration.",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true,
                oldComment: "PII: full OrderConfirmedEvent serialised to JSON for issuance-time hydration.");

            migrationBuilder.AlterColumn<Guid>(
                name: "issued_invoice_id",
                schema: "invoicing",
                table: "pending_invoices",
                type: "uuid",
                nullable: true,
                comment: "Set by M7's IssueInvoiceCommandHandler atomically with the Invoice aggregate insert.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Set by IssueInvoiceCommandHandler atomically with the Invoice aggregate insert.");

            migrationBuilder.AlterColumn<Guid>(
                name: "buyer_id",
                schema: "invoicing",
                table: "pending_invoices",
                type: "uuid",
                nullable: true,
                comment: "OrderConfirmedEvent.BuyerId; M7's outbox publisher uses this as the partition key on invoicing.invoices.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "OrderConfirmedEvent.BuyerId; the outbox publisher uses this as the partition key on invoicing.invoices.");

            migrationBuilder.AlterColumn<string>(
                name: "payment_payload",
                schema: "invoicing",
                table: "pending_credit_notes",
                type: "jsonb",
                nullable: true,
                comment: "Full PaymentRefundedEvent serialised to JSON for M7 hydration.",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true,
                oldComment: "Full PaymentRefundedEvent serialised to JSON for issuance-time hydration.");

            migrationBuilder.AlterColumn<string>(
                name: "order_payload",
                schema: "invoicing",
                table: "pending_credit_notes",
                type: "jsonb",
                nullable: true,
                comment: "PII: full OrderCancelledEvent serialised to JSON for M7 hydration.",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true,
                oldComment: "PII: full OrderCancelledEvent serialised to JSON for issuance-time hydration.");

            migrationBuilder.AlterColumn<Guid>(
                name: "issued_credit_note_id",
                schema: "invoicing",
                table: "pending_credit_notes",
                type: "uuid",
                nullable: true,
                comment: "Set by M7's IssueCreditNoteCommandHandler atomically with the CreditNote aggregate insert.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Set by IssueCreditNoteCommandHandler atomically with the CreditNote aggregate insert.");

            migrationBuilder.AlterColumn<Guid>(
                name: "buyer_id",
                schema: "invoicing",
                table: "pending_credit_notes",
                type: "uuid",
                nullable: true,
                comment: "OrderCancelledEvent.BuyerId; M7's outbox publisher uses this as the partition key.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "OrderCancelledEvent.BuyerId; the outbox publisher uses this as the partition key.");

            migrationBuilder.AlterColumn<Guid>(
                name: "correlation_id",
                schema: "invoicing",
                table: "credit_notes",
                type: "uuid",
                nullable: false,
                comment: "Cancellation flow correlation id; used as M7 idempotency key.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Cancellation flow correlation id; used as idempotency key.");
        }
    }
}
