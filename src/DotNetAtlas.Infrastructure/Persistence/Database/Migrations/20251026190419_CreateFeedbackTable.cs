using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetAtlas.Infrastructure.Persistence.Database.Migrations;

/// <inheritdoc />
public partial class CreateFeedbackTable : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "weather");

        migrationBuilder.CreateTable(
            name: "Feedbacks",
            schema: "weather",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "PK"),
                CreatedByUser = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "User who created the feedback."),
                CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "Creation timestamp (UTC)."),
                LastModifiedUtc = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "Last modification timestamp (UTC)."),
                Feedback = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, comment: "Weather feedback from the user."),
                Rating = table.Column<int>(type: "int", nullable: false, comment: "Rating given by the user."),
                Timestamp = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true, comment: "Optimistic concurrency token.")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Feedbacks", x => x.Id);
            },
            comment: "Contains user feedbacks about the weather.");

        migrationBuilder.CreateIndex(
            name: "UX_WeatherFeedback_CreatedByUser",
            schema: "weather",
            table: "Feedbacks",
            column: "CreatedByUser",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Feedbacks",
            schema: "weather");
    }
}
