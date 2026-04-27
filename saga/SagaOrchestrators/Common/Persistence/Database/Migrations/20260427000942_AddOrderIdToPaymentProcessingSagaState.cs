using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SagaOrchestrators.Common.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderIdToPaymentProcessingSagaState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "order_id",
                schema: "saga",
                table: "PaymentProcessingSagaState",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                comment: "Ordering aggregate id this payment is attached to. Frozen at saga start.");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSagaState_OrderId",
                schema: "saga",
                table: "PaymentProcessingSagaState",
                column: "order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentSagaState_OrderId",
                schema: "saga",
                table: "PaymentProcessingSagaState");

            migrationBuilder.DropColumn(
                name: "order_id",
                schema: "saga",
                table: "PaymentProcessingSagaState");
        }
    }
}
