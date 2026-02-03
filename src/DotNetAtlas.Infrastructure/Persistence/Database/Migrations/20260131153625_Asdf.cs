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
