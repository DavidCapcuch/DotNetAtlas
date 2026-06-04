using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalog.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class DropDedicatedCorrelationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "correlation_id",
                schema: "catalog",
                table: "product_search_view");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "correlation_id",
                schema: "catalog",
                table: "product_search_view",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                comment: "Originating HTTP correlation id (ADR-0008). Populated from HttpContext.Items[CorrelationIdContextKeys.HttpContextItemsKey] by the API layer, or Guid.Empty when no HTTP pipeline is in play.");
        }
    }
}
