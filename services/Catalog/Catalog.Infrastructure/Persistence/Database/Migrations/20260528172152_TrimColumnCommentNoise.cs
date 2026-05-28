using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalog.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class TrimColumnCommentNoise : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "is_sellable",
                schema: "catalog",
                table: "product_search_view",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Computed flag — wired up by the StockLevelChanged Kafka inbox consumer.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false,
                oldComment: "Computed flag — wired up by the StockLevelChanged Kafka inbox consumer (M4.2).");

            migrationBuilder.AlterColumn<Guid>(
                name: "correlation_id",
                schema: "catalog",
                table: "product_search_view",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                comment: "Originating HTTP correlation id (ADR-0008). Populated from HttpContext.Items[CorrelationIdContextKeys.HttpContextItemsKey] by the API layer, or Guid.Empty when no HTTP pipeline is in play.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldDefaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldComment: "Originating HTTP correlation id (ADR-0008). M4 reserves the column; the API layer wires HttpContext.Items[CorrelationIdContextKeys.HttpContextItemsKey] in M6.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "is_sellable",
                schema: "catalog",
                table: "product_search_view",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Computed flag — wired up by the StockLevelChanged Kafka inbox consumer (M4.2).",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false,
                oldComment: "Computed flag — wired up by the StockLevelChanged Kafka inbox consumer.");

            migrationBuilder.AlterColumn<Guid>(
                name: "correlation_id",
                schema: "catalog",
                table: "product_search_view",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                comment: "Originating HTTP correlation id (ADR-0008). M4 reserves the column; the API layer wires HttpContext.Items[CorrelationIdContextKeys.HttpContextItemsKey] in M6.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldDefaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldComment: "Originating HTTP correlation id (ADR-0008). Populated from HttpContext.Items[CorrelationIdContextKeys.HttpContextItemsKey] by the API layer, or Guid.Empty when no HTTP pipeline is in play.");
        }
    }
}
