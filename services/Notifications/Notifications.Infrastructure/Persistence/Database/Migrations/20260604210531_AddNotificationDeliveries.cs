using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notifications.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_deliveries",
                schema: "notifications",
                columns: table => new
                {
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Producer-assigned notification intent identity (half of the ledger key)."),
                    channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "Delivery channel (Email|Sms|Bell) — the other half of the ledger key."),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "Latest recorded outcome (Dispatched|Failed)."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the row was first inserted."),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp of the latest status write.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_deliveries", x => new { x.notification_id, x.channel });
                },
                comment: "Per-channel delivery ledger — idempotency + audit, keyed (notification_id, channel). ADR-0031/0032.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_deliveries",
                schema: "notifications");
        }
    }
}
