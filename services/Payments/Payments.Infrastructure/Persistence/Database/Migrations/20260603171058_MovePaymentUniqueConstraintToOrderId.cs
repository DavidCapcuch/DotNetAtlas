using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class MovePaymentUniqueConstraintToOrderId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payment_transactions_order_id",
                schema: "payments",
                table: "payment_transactions");

            migrationBuilder.DropIndex(
                name: "ux_payment_transactions_correlation_id",
                schema: "payments",
                table: "payment_transactions");

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                schema: "payments",
                table: "payment_transactions",
                type: "uuid",
                nullable: false,
                comment: "Ordering aggregate id this payment is attached to (frozen at creation). Unique index enforces one payment per order (ADR-0029).",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Ordering aggregate id this payment is attached to (frozen at creation; debugging/admin-lookup convenience).");

            migrationBuilder.AlterColumn<Guid>(
                name: "correlation_id",
                schema: "payments",
                table: "payment_transactions",
                type: "uuid",
                nullable: false,
                comment: "Originating saga correlation id (== OrderId per ADR-0029; links checkout / order / invoice).",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Originating saga correlation id (links checkout / order / invoice). Unique index enforces one payment per saga.");

            migrationBuilder.AlterColumn<Guid>(
                name: "id",
                schema: "payments",
                table: "payment_transactions",
                type: "uuid",
                nullable: false,
                comment: "Primary key — saga-minted UUID v7 (time-ordered), carried on AuthorizePaymentCommand as PaymentTransactionId; distinct from the saga key (OrderId). One payment per order is enforced by the ux_payment_transactions_order_id unique index (ADR-0029). See docs/bc-design/payments.md § 2.2 (I-7).",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Primary key — saga-minted UUID v7 (time-ordered), carried on AuthorizePaymentCommand as PaymentTransactionId; distinct from CorrelationId. One payment per saga is enforced by the ux_payment_transactions_correlation_id unique index. See docs/bc-design/payments.md § 2.2 (I-7).");

            migrationBuilder.CreateIndex(
                name: "ux_payment_transactions_order_id",
                schema: "payments",
                table: "payment_transactions",
                column: "order_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_payment_transactions_order_id",
                schema: "payments",
                table: "payment_transactions");

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                schema: "payments",
                table: "payment_transactions",
                type: "uuid",
                nullable: false,
                comment: "Ordering aggregate id this payment is attached to (frozen at creation; debugging/admin-lookup convenience).",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Ordering aggregate id this payment is attached to (frozen at creation). Unique index enforces one payment per order (ADR-0029).");

            migrationBuilder.AlterColumn<Guid>(
                name: "correlation_id",
                schema: "payments",
                table: "payment_transactions",
                type: "uuid",
                nullable: false,
                comment: "Originating saga correlation id (links checkout / order / invoice). Unique index enforces one payment per saga.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Originating saga correlation id (== OrderId per ADR-0029; links checkout / order / invoice).");

            migrationBuilder.AlterColumn<Guid>(
                name: "id",
                schema: "payments",
                table: "payment_transactions",
                type: "uuid",
                nullable: false,
                comment: "Primary key — saga-minted UUID v7 (time-ordered), carried on AuthorizePaymentCommand as PaymentTransactionId; distinct from CorrelationId. One payment per saga is enforced by the ux_payment_transactions_correlation_id unique index. See docs/bc-design/payments.md § 2.2 (I-7).",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Primary key — saga-minted UUID v7 (time-ordered), carried on AuthorizePaymentCommand as PaymentTransactionId; distinct from the saga key (OrderId). One payment per order is enforced by the ux_payment_transactions_order_id unique index (ADR-0029). See docs/bc-design/payments.md § 2.2 (I-7).");

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_order_id",
                schema: "payments",
                table: "payment_transactions",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ux_payment_transactions_correlation_id",
                schema: "payments",
                table: "payment_transactions",
                column: "correlation_id",
                unique: true);
        }
    }
}
