using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SagaOrchestrators.Common.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class DropSagaOrderId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payment_processing_saga_state_order_id",
                schema: "saga",
                table: "payment_processing_saga_state");

            migrationBuilder.DropIndex(
                name: "ix_checkout_saga_state_order_id",
                schema: "saga",
                table: "checkout_saga_state");

            migrationBuilder.DropColumn(
                name: "order_id",
                schema: "saga",
                table: "payment_processing_saga_state");

            migrationBuilder.DropColumn(
                name: "order_id",
                schema: "saga",
                table: "checkout_saga_state");

            migrationBuilder.AlterColumn<Guid>(
                name: "correlation_id",
                schema: "saga",
                table: "payment_processing_saga_state",
                type: "uuid",
                nullable: false,
                comment: "MassTransit saga instance id (ISaga.CorrelationId); equals the pre-assigned OrderId (ADR-0029).",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Unique correlation ID for the payment saga");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "correlation_id",
                schema: "saga",
                table: "payment_processing_saga_state",
                type: "uuid",
                nullable: false,
                comment: "Unique correlation ID for the payment saga",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "MassTransit saga instance id (ISaga.CorrelationId); equals the pre-assigned OrderId (ADR-0029).");

            migrationBuilder.AddColumn<Guid>(
                name: "order_id",
                schema: "saga",
                table: "payment_processing_saga_state",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                comment: "Ordering aggregate id this payment is attached to. Frozen at saga start.");

            migrationBuilder.AddColumn<Guid>(
                name: "order_id",
                schema: "saga",
                table: "checkout_saga_state",
                type: "uuid",
                nullable: true,
                comment: "Ordering aggregate id assigned after OrderCreatedEvent. Null until OrderCreated arrives.");

            migrationBuilder.CreateIndex(
                name: "ix_payment_processing_saga_state_order_id",
                schema: "saga",
                table: "payment_processing_saga_state",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_checkout_saga_state_order_id",
                schema: "saga",
                table: "checkout_saga_state",
                column: "order_id");
        }
    }
}
