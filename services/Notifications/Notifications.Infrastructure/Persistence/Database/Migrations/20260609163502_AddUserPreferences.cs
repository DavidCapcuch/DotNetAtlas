using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notifications.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_preferences",
                schema: "notifications",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Recipient identity — the Keycloak sub; equals the command's RecipientUserId."),
                    email = table.Column<string>(type: "text", nullable: false, comment: "Email address the email dispatcher delivers to."),
                    phone_number = table.Column<string>(type: "text", nullable: false, comment: "Fake E.164 phone number (SMS is a fake channel); consumed by the SMS dispatcher (#315)."),
                    enabled_channels = table.Column<string[]>(type: "text[]", nullable: false, comment: "Channels the recipient enabled — the left operand of enabled ∩ template_channels (§5.3)."),
                    quiet_hours_start = table.Column<TimeOnly>(type: "time", nullable: true, comment: "Start of the daily quiet-hours window (civil wall-clock in time_zone); null = no quiet hours."),
                    quiet_hours_end = table.Column<TimeOnly>(type: "time", nullable: true, comment: "End of the quiet-hours window; null with quiet_hours_start (both-or-neither)."),
                    time_zone = table.Column<string>(type: "text", nullable: false, comment: "IANA time zone (e.g. Europe/Prague) the quiet-hours window is interpreted in.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_preferences", x => x.user_id);
                },
                comment: "Seeded recipient preference + contact reference, keyed user_id (Keycloak sub). notifications.md §8.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_preferences",
                schema: "notifications");
        }
    }
}
