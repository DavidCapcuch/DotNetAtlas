using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Weather.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "weather");

            migrationBuilder.CreateTable(
                name: "alert_subscribers",
                schema: "weather",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "PK"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "User who subscribed for weather alerts."),
                    subscription_tier = table.Column<int>(type: "integer", nullable: false, comment: "Subscription tier (Free, Pro, Ultra)."),
                    subscription_expiry_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Expiry date for subscription (UTC). Null for free tier."),
                    last_paid_subscription_ended_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "When the last paid subscription ended. Null if never had paid subscription."),
                    temperature_unit_preference = table.Column<int>(type: "integer", nullable: false, comment: "Preferred temperature unit (Celsius, Fahrenheit, Kelvin)."),
                    wind_speed_unit_preference = table.Column<int>(type: "integer", nullable: false, comment: "Preferred wind speed unit (KilometersPerHour, MilesPerHour)."),
                    created_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Timestamp when user first subscribed (UTC)."),
                    last_modified_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Timestamp when subscription was last modified (UTC)."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false, comment: "Optimistic concurrency token.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alert_subscribers", x => x.id);
                },
                comment: "Contains subscribers for weather alert subscriptions.");

            migrationBuilder.CreateTable(
                name: "feedbacks",
                schema: "weather",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "PK"),
                    created_by_user = table.Column<Guid>(type: "uuid", nullable: false, comment: "User who created the feedback."),
                    created_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Creation timestamp (UTC)."),
                    last_modified_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Last modification timestamp (UTC)."),
                    Feedback = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "Weather feedback from the user."),
                    Rating = table.Column<byte>(type: "smallint", nullable: false, comment: "Rating given by the user."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false, comment: "Optimistic concurrency token.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedbacks", x => x.id);
                },
                comment: "Contains user feedbacks about the weather.");

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "weather",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Unique message identifier (Primary Key)."),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the message was processed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => x.message_id);
                },
                comment: "Inbox pattern table for idempotent message processing. Tracks processed messages to prevent duplicate processing.");

            migrationBuilder.CreateTable(
                name: "locations",
                schema: "weather",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "PK"),
                    country_code = table.Column<int>(type: "integer", nullable: false, comment: "ISO 3166-1 alpha-2 country code."),
                    created_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Creation timestamp (UTC)."),
                    last_modified_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Last modification timestamp (UTC)."),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Name of the city."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false, comment: "Optimistic concurrency token.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_locations", x => x.id);
                },
                comment: "Contains city-country locations.");

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "weather",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, comment: "PK, Identity")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    topic_name = table.Column<string>(type: "character varying(249)", unicode: false, maxLength: 249, nullable: false, comment: "The Kafka topic where this message will be published. Set by the message producer."),
                    kafka_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, comment: "Kafka Key - typically the Aggregate ID for proper event ordering and partitioning"),
                    avro_payload = table.Column<byte[]>(type: "bytea", nullable: false, comment: "Avro-serialized domain event payload"),
                    type = table.Column<string>(type: "character varying(255)", unicode: false, maxLength: 255, nullable: false, comment: "Avro type name of the serialized event (e.g., 'FeedbackChangedEvent') for deserialization and observability"),
                    headers = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true, comment: "JSON dictionary of OpenTelemetry-standard headers for distributed tracing and metadata. Headers are automatically generated by OpenTelemetry propagators for end-to-end trace context propagation."),
                    created_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Creation timestamp (UTC).")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                },
                comment: "Outbox pattern table for storing domain events as Avro-serialized messages for reliable event publishing.");

            migrationBuilder.CreateTable(
                name: "monitored_location_alerts_subscriptions",
                schema: "weather",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "PK"),
                    monitored_location_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "FK to MonitoredLocation (ID reference only, no navigation)."),
                    created_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Creation timestamp (UTC)."),
                    last_modified_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Last modification timestamp (UTC)."),
                    alert_subscriber_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false, comment: "Optimistic concurrency token.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitored_location_alerts_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_monitored_location_alerts_subscriptions_alert_subscribers_a",
                        column: x => x.alert_subscriber_id,
                        principalSchema: "weather",
                        principalTable: "alert_subscribers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Contains user subscriptions to monitored location weather alerts.");

            migrationBuilder.CreateTable(
                name: "monitored_locations",
                schema: "weather",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "PK"),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether this location is actively being monitored."),
                    created_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Creation timestamp (UTC)."),
                    last_modified_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Last modification timestamp (UTC)."),
                    HighHumidityThresholdPercent = table.Column<double>(type: "double precision", nullable: false, comment: "Humidity threshold for high humidity alerts (%)."),
                    HighTemperatureThresholdC = table.Column<double>(type: "double precision", nullable: false, comment: "Temperature threshold for high temperature alerts (°C)."),
                    HighWindSpeedThresholdKmh = table.Column<double>(type: "double precision", nullable: false, comment: "Wind speed threshold for high wind alerts (km/h)."),
                    LowHumidityThresholdPercent = table.Column<double>(type: "double precision", nullable: false, comment: "Humidity threshold for low humidity alerts (%)."),
                    LowTemperatureThresholdC = table.Column<double>(type: "double precision", nullable: false, comment: "Temperature threshold for low temperature alerts (°C)."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false, comment: "Optimistic concurrency token."),
                    recent_readings = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitored_locations", x => x.id);
                    table.ForeignKey(
                        name: "fk_monitored_locations_locations_location_id",
                        column: x => x.location_id,
                        principalSchema: "weather",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Contains monitored locations with weather sensor data and alert thresholds.");

            migrationBuilder.CreateIndex(
                name: "IX_Subscribers_SubscriptionTier_ExpiryUtc",
                schema: "weather",
                table: "alert_subscribers",
                columns: new[] { "subscription_tier", "subscription_expiry_at_utc" });

            migrationBuilder.CreateIndex(
                name: "UX_Subscribers_UserId",
                schema: "weather",
                table: "alert_subscribers",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_WeatherFeedback_CreatedByUser",
                schema: "weather",
                table: "feedbacks",
                column: "created_by_user",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_ProcessedAtUtc",
                schema: "weather",
                table: "InboxMessages",
                column: "processed_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_monitored_location_alerts_subscriptions_alert_subscriber_id",
                schema: "weather",
                table: "monitored_location_alerts_subscriptions",
                column: "alert_subscriber_id");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredLocationAlertsSubscriptions_MonitoredLocationId",
                schema: "weather",
                table: "monitored_location_alerts_subscriptions",
                column: "monitored_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitored_locations_location_id",
                schema: "weather",
                table: "monitored_locations",
                column: "location_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feedbacks",
                schema: "weather");

            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "weather");

            migrationBuilder.DropTable(
                name: "monitored_location_alerts_subscriptions",
                schema: "weather");

            migrationBuilder.DropTable(
                name: "monitored_locations",
                schema: "weather");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "weather");

            migrationBuilder.DropTable(
                name: "alert_subscribers",
                schema: "weather");

            migrationBuilder.DropTable(
                name: "locations",
                schema: "weather");
        }
    }
}
