using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notifications.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "templates",
                schema: "notifications",
                columns: table => new
                {
                    template_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Template identity {bounded-context}.{notification-type} (lower-kebab)."),
                    description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Human-readable description of what this template notifies about.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_templates", x => x.template_key);
                },
                comment: "Seeded notification template reference data, keyed {bc}.{type} (lower-kebab). ADR-0032 §7.");

            migrationBuilder.CreateTable(
                name: "template_channels",
                schema: "notifications",
                columns: table => new
                {
                    template_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Owning template's key (FK to templates.template_key)."),
                    channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "Delivery channel (Email|Sms|Bell) this content renders for."),
                    subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "Subject-line template with {{token}} placeholders; null for channels without a subject."),
                    body = table.Column<string>(type: "text", nullable: false, comment: "Body template with {{token}} placeholders.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_channels", x => new { x.template_key, x.channel });
                    table.ForeignKey(
                        name: "fk_template_channels_templates_template_key",
                        column: x => x.template_key,
                        principalSchema: "notifications",
                        principalTable: "templates",
                        principalColumn: "template_key",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Per-channel template content + the supported-channel set, keyed (template_key, channel_type). ADR-0032 §7.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "template_channels",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "templates",
                schema: "notifications");
        }
    }
}
