using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invoicing.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class DropDedicatedCorrelationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_pending_invoices",
                schema: "invoicing",
                table: "pending_invoices");

            migrationBuilder.DropIndex(
                name: "ix_pending_invoices_order_id",
                schema: "invoicing",
                table: "pending_invoices");

            migrationBuilder.DropPrimaryKey(
                name: "pk_pending_credit_notes",
                schema: "invoicing",
                table: "pending_credit_notes");

            migrationBuilder.DropIndex(
                name: "ix_pending_credit_notes_order_id",
                schema: "invoicing",
                table: "pending_credit_notes");

            migrationBuilder.DropIndex(
                name: "ux_invoices_correlation_id",
                schema: "invoicing",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "ux_credit_notes_correlation_id",
                schema: "invoicing",
                table: "credit_notes");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                schema: "invoicing",
                table: "pending_invoices");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                schema: "invoicing",
                table: "pending_credit_notes");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                schema: "invoicing",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                schema: "invoicing",
                table: "credit_notes");

            migrationBuilder.AlterTable(
                name: "pending_invoices",
                schema: "invoicing",
                comment: "Async-enrichment buffer: collects OrderConfirmedEvent + PaymentCapturedEvent halves keyed on OrderId until IssueInvoiceCommandHandler converts the converged row into an Invoice aggregate.",
                oldComment: "Async-enrichment buffer: collects OrderConfirmedEvent + PaymentCapturedEvent halves keyed on CorrelationId until IssueInvoiceCommandHandler converts the converged row into an Invoice aggregate.");

            migrationBuilder.AlterTable(
                name: "pending_credit_notes",
                schema: "invoicing",
                comment: "Async-enrichment buffer: collects OrderCancelledEvent + PaymentRefundedEvent halves keyed on OrderId until IssueCreditNoteCommandHandler converts the converged row into a CreditNote aggregate.",
                oldComment: "Async-enrichment buffer: collects OrderCancelledEvent + PaymentRefundedEvent halves keyed on CorrelationId until IssueCreditNoteCommandHandler converts the converged row into a CreditNote aggregate.");

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                schema: "invoicing",
                table: "pending_invoices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                comment: "OrderConfirmedEvent.OrderId; the cross-BC convergence key. Primary key.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "OrderConfirmedEvent.OrderId; null until the order half arrives.");

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                schema: "invoicing",
                table: "pending_credit_notes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                comment: "OrderCancelledEvent.OrderId; the cross-BC convergence key. Primary key.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "OrderCancelledEvent.OrderId; null until the order-cancel half arrives.");

            migrationBuilder.AddPrimaryKey(
                name: "pk_pending_invoices",
                schema: "invoicing",
                table: "pending_invoices",
                column: "order_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_pending_credit_notes",
                schema: "invoicing",
                table: "pending_credit_notes",
                column: "order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_pending_invoices",
                schema: "invoicing",
                table: "pending_invoices");

            migrationBuilder.DropPrimaryKey(
                name: "pk_pending_credit_notes",
                schema: "invoicing",
                table: "pending_credit_notes");

            migrationBuilder.AlterTable(
                name: "pending_invoices",
                schema: "invoicing",
                comment: "Async-enrichment buffer: collects OrderConfirmedEvent + PaymentCapturedEvent halves keyed on CorrelationId until IssueInvoiceCommandHandler converts the converged row into an Invoice aggregate.",
                oldComment: "Async-enrichment buffer: collects OrderConfirmedEvent + PaymentCapturedEvent halves keyed on OrderId until IssueInvoiceCommandHandler converts the converged row into an Invoice aggregate.");

            migrationBuilder.AlterTable(
                name: "pending_credit_notes",
                schema: "invoicing",
                comment: "Async-enrichment buffer: collects OrderCancelledEvent + PaymentRefundedEvent halves keyed on CorrelationId until IssueCreditNoteCommandHandler converts the converged row into a CreditNote aggregate.",
                oldComment: "Async-enrichment buffer: collects OrderCancelledEvent + PaymentRefundedEvent halves keyed on OrderId until IssueCreditNoteCommandHandler converts the converged row into a CreditNote aggregate.");

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                schema: "invoicing",
                table: "pending_invoices",
                type: "uuid",
                nullable: true,
                comment: "OrderConfirmedEvent.OrderId; null until the order half arrives.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "OrderConfirmedEvent.OrderId; the cross-BC convergence key. Primary key.");

            migrationBuilder.AddColumn<Guid>(
                name: "correlation_id",
                schema: "invoicing",
                table: "pending_invoices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                comment: "Saga / cross-BC correlation id. Primary key.");

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                schema: "invoicing",
                table: "pending_credit_notes",
                type: "uuid",
                nullable: true,
                comment: "OrderCancelledEvent.OrderId; null until the order-cancel half arrives.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "OrderCancelledEvent.OrderId; the cross-BC convergence key. Primary key.");

            migrationBuilder.AddColumn<Guid>(
                name: "correlation_id",
                schema: "invoicing",
                table: "pending_credit_notes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                comment: "Saga / cross-BC correlation id. Primary key.");

            migrationBuilder.AddColumn<Guid>(
                name: "correlation_id",
                schema: "invoicing",
                table: "invoices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                comment: "Checkout saga correlation id (passed through from Order + Payment).");

            migrationBuilder.AddColumn<Guid>(
                name: "correlation_id",
                schema: "invoicing",
                table: "credit_notes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                comment: "Cancellation flow correlation id; used as idempotency key.");

            migrationBuilder.AddPrimaryKey(
                name: "pk_pending_invoices",
                schema: "invoicing",
                table: "pending_invoices",
                column: "correlation_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_pending_credit_notes",
                schema: "invoicing",
                table: "pending_credit_notes",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_pending_invoices_order_id",
                schema: "invoicing",
                table: "pending_invoices",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_pending_credit_notes_order_id",
                schema: "invoicing",
                table: "pending_credit_notes",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ux_invoices_correlation_id",
                schema: "invoicing",
                table: "invoices",
                column: "correlation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_credit_notes_correlation_id",
                schema: "invoicing",
                table: "credit_notes",
                column: "correlation_id",
                unique: true);
        }
    }
}
