using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class DropDedicatedCorrelationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stock_events_correlation",
                schema: "inventory",
                table: "stock_events");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                schema: "inventory",
                table: "stock_events");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "correlation_id",
                schema: "inventory",
                table: "stock_events",
                type: "uuid",
                nullable: true,
                comment: "Saga correlation id (ADR-0008); null for ops-originated events.");

            migrationBuilder.CreateIndex(
                name: "ix_stock_events_correlation",
                schema: "inventory",
                table: "stock_events",
                column: "correlation_id",
                filter: "correlation_id IS NOT NULL");
        }
    }
}
