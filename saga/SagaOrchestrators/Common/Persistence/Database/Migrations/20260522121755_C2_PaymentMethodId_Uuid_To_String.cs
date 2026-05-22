using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SagaOrchestrators.Common.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class C2_PaymentMethodId_Uuid_To_String : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "payment_method_id",
                schema: "saga",
                table: "PaymentProcessingSagaState",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "Gateway-issued opaque payment-method token (Stripe 'pm_*', Adyen alphanumeric, …); 1-64 chars. Changed from uuid in the Wave-1 closeout C-2 fix.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "ID of the saved payment method");

            migrationBuilder.AlterColumn<Guid>(
                name: "payment_method_id",
                schema: "saga",
                table: "CheckoutSagaState",
                type: "uuid",
                nullable: false,
                comment: "Saved payment method id (Guid). Stored as uuid because Basket + Ordering wire shapes still use Guid; CheckoutSaga string-encodes it only at the Payments-emit boundary (C-2 closeout — Payments-side schema changed, upstream BC wire shapes deferred).",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Saved payment method id - passed through to PaymentProcessingSaga.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "payment_method_id",
                schema: "saga",
                table: "PaymentProcessingSagaState",
                type: "uuid",
                nullable: false,
                comment: "ID of the saved payment method",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "Gateway-issued opaque payment-method token (Stripe 'pm_*', Adyen alphanumeric, …); 1-64 chars. Changed from uuid in the Wave-1 closeout C-2 fix.");

            migrationBuilder.AlterColumn<Guid>(
                name: "payment_method_id",
                schema: "saga",
                table: "CheckoutSagaState",
                type: "uuid",
                nullable: false,
                comment: "Saved payment method id - passed through to PaymentProcessingSaga.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Saved payment method id (Guid). Stored as uuid because Basket + Ordering wire shapes still use Guid; CheckoutSaga string-encodes it only at the Payments-emit boundary (C-2 closeout — Payments-side schema changed, upstream BC wire shapes deferred).");
        }
    }
}
