using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddStockEventsEventSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateTable(
                name: "stock_events",
                schema: "inventory",
                columns: table => new
                {
                    stream_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Stream identity = ProductId. One stream per StockItem."),
                    version = table.Column<int>(type: "integer", nullable: false, comment: "Monotonic 1-based version per stream. Enforced by PK."),
                    event_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "CLR-type name discriminator (e.g. \"StockReservedEvent\") used by the deserializer."),
                    payload = table.Column<string>(type: "jsonb", nullable: false, comment: "JSON-serialized internal event; stored as jsonb for legibility and indexability."),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp the domain event was produced; copied from event.OccurredOnUtc for temporal queries."),
                    appended_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()", comment: "DB-side insert timestamp; distinguishes domain time from persisted time during replay/tests."),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Saga correlation id (ADR-0008); null for ops-originated events.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_events", x => new { x.stream_id, x.version });
                },
                comment: "Append-only event store for StockItem aggregates (ADR-0006). One row per internal ES event; composite PK (StreamId, Version) is the optimistic-concurrency mechanism.");

            migrationBuilder.CreateIndex(
                name: "ix_stock_events_correlation",
                schema: "inventory",
                table: "stock_events",
                column: "correlation_id",
                filter: "correlation_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_events_event_type",
                schema: "inventory",
                table: "stock_events",
                column: "event_type");

            migrationBuilder.CreateIndex(
                name: "ix_stock_events_occurred_at",
                schema: "inventory",
                table: "stock_events",
                column: "occurred_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_events",
                schema: "inventory");
        }
    }
}
