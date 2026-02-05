using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetAtlas.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class Asdf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Type",
                schema: "weather",
                table: "OutboxMessages",
                type: "varchar(255)",
                unicode: false,
                maxLength: 255,
                nullable: false,
                comment: "Avro type name of the serialized event (e.g., 'FeedbackChangedEvent') for deserialization and observability",
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldUnicode: false,
                oldMaxLength: 255,
                oldComment: "Avro type name of the serialized event (e.g., 'FeedbackChangedEvent') used for type-based topic routing");

            migrationBuilder.AddColumn<string>(
                name: "TopicName",
                schema: "weather",
                table: "OutboxMessages",
                type: "varchar(249)",
                unicode: false,
                maxLength: 249,
                nullable: false,
                defaultValue: "",
                comment: "The Kafka topic where this message will be published. Set by the message producer.");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LastPaidSubscriptionEndedAtUtc",
                schema: "weather",
                table: "AlertSubscribers",
                type: "datetimeoffset",
                nullable: true,
                comment: "When the last paid subscription ended. Null if never had paid subscription.",
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TopicName",
                schema: "weather",
                table: "OutboxMessages");

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                schema: "weather",
                table: "OutboxMessages",
                type: "varchar(255)",
                unicode: false,
                maxLength: 255,
                nullable: false,
                comment: "Avro type name of the serialized event (e.g., 'FeedbackChangedEvent') used for type-based topic routing",
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldUnicode: false,
                oldMaxLength: 255,
                oldComment: "Avro type name of the serialized event (e.g., 'FeedbackChangedEvent') for deserialization and observability");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LastPaidSubscriptionEndedAtUtc",
                schema: "weather",
                table: "AlertSubscribers",
                type: "datetimeoffset",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true,
                oldComment: "When the last paid subscription ended. Null if never had paid subscription.");
        }
    }
}
