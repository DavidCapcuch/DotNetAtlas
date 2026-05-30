using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class UpdateStockLevelCommentToEventSuffix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "previous_available",
                schema: "inventory",
                table: "current_stock_levels",
                type: "integer",
                nullable: false,
                comment: "Available BEFORE the last applied event; enables StockLevelChangedEvent threshold detection without state replay.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Available BEFORE the last applied event; enables StockLevelChanged threshold detection without state replay.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "previous_available",
                schema: "inventory",
                table: "current_stock_levels",
                type: "integer",
                nullable: false,
                comment: "Available BEFORE the last applied event; enables StockLevelChanged threshold detection without state replay.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Available BEFORE the last applied event; enables StockLevelChangedEvent threshold detection without state replay.");
        }
    }
}
