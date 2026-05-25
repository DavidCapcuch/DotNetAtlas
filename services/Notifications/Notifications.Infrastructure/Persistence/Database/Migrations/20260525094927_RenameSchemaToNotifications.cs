using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notifications.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class RenameSchemaToNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.RenameTable(
                name: "OutboxMessages",
                schema: "payment",
                newName: "OutboxMessages",
                newSchema: "notifications");

            migrationBuilder.RenameTable(
                name: "InboxMessages",
                schema: "payment",
                newName: "InboxMessages",
                newSchema: "notifications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payment");

            migrationBuilder.RenameTable(
                name: "OutboxMessages",
                schema: "notifications",
                newName: "OutboxMessages",
                newSchema: "payment");

            migrationBuilder.RenameTable(
                name: "InboxMessages",
                schema: "notifications",
                newName: "InboxMessages",
                newSchema: "payment");
        }
    }
}
