using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalog.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class UpdateIsSellableCommentToStockLevelChangedEvent : Migration
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
                comment: "Computed flag — wired up by the StockLevelChangedEvent Kafka inbox consumer.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false,
                oldComment: "Computed flag — wired up by the StockLevelChanged Kafka inbox consumer.");
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
                comment: "Computed flag — wired up by the StockLevelChanged Kafka inbox consumer.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false,
                oldComment: "Computed flag — wired up by the StockLevelChangedEvent Kafka inbox consumer.");
        }
    }
}
