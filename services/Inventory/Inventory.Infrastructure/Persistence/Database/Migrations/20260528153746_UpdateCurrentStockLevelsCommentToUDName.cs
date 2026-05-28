using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class UpdateCurrentStockLevelsCommentToUDName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "current_stock_levels",
                schema: "inventory",
                comment: "Denormalised read projection: one row per ProductId, mutated by CurrentStockLevelsProjectionDomainEventHandler on every ES event. Rebuildable from inventory.stock_events.",
                oldComment: "Denormalised read projection: one row per ProductId, mutated by CurrentStockLevelsProjectionHandler on every ES event. Rebuildable from inventory.stock_events.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "current_stock_levels",
                schema: "inventory",
                comment: "Denormalised read projection: one row per ProductId, mutated by CurrentStockLevelsProjectionHandler on every ES event. Rebuildable from inventory.stock_events.",
                oldComment: "Denormalised read projection: one row per ProductId, mutated by CurrentStockLevelsProjectionDomainEventHandler on every ES event. Rebuildable from inventory.stock_events.");
        }
    }
}
