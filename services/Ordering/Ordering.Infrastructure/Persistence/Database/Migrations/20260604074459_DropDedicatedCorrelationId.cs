using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ordering.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class DropDedicatedCorrelationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_orders_correlation_id",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                schema: "ordering",
                table: "orders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "correlation_id",
                schema: "ordering",
                table: "orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                comment: "Checkout saga correlation id. Idempotency key for CreateOrderCommand.");

            migrationBuilder.CreateIndex(
                name: "ux_orders_correlation_id",
                schema: "ordering",
                table: "orders",
                column: "correlation_id",
                unique: true);
        }
    }
}
