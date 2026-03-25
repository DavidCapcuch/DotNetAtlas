using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weather.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertAndInboxTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte>(
                name: "Rating",
                schema: "weather",
                table: "Feedbacks",
                type: "tinyint",
                nullable: false,
                comment: "Rating given by the user.",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "Rating given by the user.");

            migrationBuilder.CreateTable(
                name: "AlertSubscribers",
                schema: "weather",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "PK"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "User who subscribed for weather alerts."),
                    SubscriptionTier = table.Column<int>(type: "int", nullable: false, comment: "Subscription tier (Free, Pro, Ultra)."),
                    SubscriptionExpiryAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true, comment: "Expiry date for subscription (UTC). Null for free tier."),
                    LastPaidSubscriptionEndedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TemperatureUnitPreference = table.Column<int>(type: "int", nullable: false, comment: "Preferred temperature unit (Celsius, Fahrenheit, Kelvin)."),
                    WindSpeedUnitPreference = table.Column<int>(type: "int", nullable: false, comment: "Preferred wind speed unit (KilometersPerHour, MilesPerHour)."),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, comment: "Timestamp when user first subscribed (UTC)."),
                    LastModifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, comment: "Timestamp when subscription was last modified (UTC)."),
                    Timestamp = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true, comment: "Optimistic concurrency token.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertSubscribers", x => x.Id);
                },
                comment: "Contains subscribers for weather alert subscriptions.");

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "weather",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "Unique message identifier (Primary Key)."),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, comment: "UTC timestamp when the message was processed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => x.MessageId);
                },
                comment: "Inbox pattern table for idempotent message processing. Tracks processed messages to prevent duplicate processing.");

            migrationBuilder.CreateTable(
                name: "Locations",
                schema: "weather",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "PK"),
                    CountryCode = table.Column<int>(type: "int", nullable: false, comment: "ISO 3166-1 alpha-2 country code."),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, comment: "Creation timestamp (UTC)."),
                    LastModifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, comment: "Last modification timestamp (UTC)."),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, comment: "Name of the city."),
                    Timestamp = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true, comment: "Optimistic concurrency token.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                },
                comment: "Contains city-country locations.");

            migrationBuilder.CreateTable(
                name: "MonitoredLocationAlertsSubscriptions",
                schema: "weather",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "PK"),
                    MonitoredLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "FK to MonitoredLocation (ID reference only, no navigation)."),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, comment: "Creation timestamp (UTC)."),
                    LastModifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, comment: "Last modification timestamp (UTC)."),
                    AlertSubscriberId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Timestamp = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true, comment: "Optimistic concurrency token.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoredLocationAlertsSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonitoredLocationAlertsSubscriptions_AlertSubscribers_AlertSubscriberId",
                        column: x => x.AlertSubscriberId,
                        principalSchema: "weather",
                        principalTable: "AlertSubscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Contains user subscriptions to monitored location weather alerts.");

            migrationBuilder.CreateTable(
                name: "MonitoredLocations",
                schema: "weather",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "PK"),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, comment: "Whether this location is actively being monitored."),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, comment: "Creation timestamp (UTC)."),
                    LastModifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, comment: "Last modification timestamp (UTC)."),
                    HighHumidityThresholdPercent = table.Column<double>(type: "float", nullable: false, comment: "Humidity threshold for high humidity alerts (%)."),
                    HighTemperatureThresholdC = table.Column<double>(type: "float", nullable: false, comment: "Temperature threshold for high temperature alerts (°C)."),
                    HighWindSpeedThresholdKmh = table.Column<double>(type: "float", nullable: false, comment: "Wind speed threshold for high wind alerts (km/h)."),
                    LowHumidityThresholdPercent = table.Column<double>(type: "float", nullable: false, comment: "Humidity threshold for low humidity alerts (%)."),
                    LowTemperatureThresholdC = table.Column<double>(type: "float", nullable: false, comment: "Temperature threshold for low temperature alerts (°C)."),
                    Timestamp = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true, comment: "Optimistic concurrency token."),
                    RecentReadings = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoredLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonitoredLocations_Locations_LocationId",
                        column: x => x.LocationId,
                        principalSchema: "weather",
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Contains monitored locations with weather sensor data and alert thresholds.");

            migrationBuilder.CreateIndex(
                name: "IX_Subscribers_SubscriptionTier_ExpiryUtc",
                schema: "weather",
                table: "AlertSubscribers",
                columns: new[] { "SubscriptionTier", "SubscriptionExpiryAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Subscribers_UserId",
                schema: "weather",
                table: "AlertSubscribers",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_ProcessedAtUtc",
                schema: "weather",
                table: "InboxMessages",
                column: "ProcessedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredLocationAlertsSubscriptions_AlertSubscriberId",
                schema: "weather",
                table: "MonitoredLocationAlertsSubscriptions",
                column: "AlertSubscriberId");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredLocationAlertsSubscriptions_MonitoredLocationId",
                schema: "weather",
                table: "MonitoredLocationAlertsSubscriptions",
                column: "MonitoredLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredLocations_LocationId",
                schema: "weather",
                table: "MonitoredLocations",
                column: "LocationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "weather");

            migrationBuilder.DropTable(
                name: "MonitoredLocationAlertsSubscriptions",
                schema: "weather");

            migrationBuilder.DropTable(
                name: "MonitoredLocations",
                schema: "weather");

            migrationBuilder.DropTable(
                name: "AlertSubscribers",
                schema: "weather");

            migrationBuilder.DropTable(
                name: "Locations",
                schema: "weather");

            migrationBuilder.AlterColumn<int>(
                name: "Rating",
                schema: "weather",
                table: "Feedbacks",
                type: "int",
                nullable: false,
                comment: "Rating given by the user.",
                oldClrType: typeof(byte),
                oldType: "tinyint",
                oldComment: "Rating given by the user.");
        }
    }
}
