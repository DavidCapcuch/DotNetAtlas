using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invoicing.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceDeliveryNotificationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "delivery_notification_id",
                schema: "invoicing",
                table: "invoices",
                type: "uuid",
                nullable: true,
                comment: "NotificationId (ADR-0031) minted when delivery was requested; correlates the delivery confirmation. Null until Issued with a delivery channel.");

            migrationBuilder.CreateIndex(
                name: "ux_invoices_delivery_notification_id",
                schema: "invoicing",
                table: "invoices",
                column: "delivery_notification_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_invoices_delivery_notification_id",
                schema: "invoicing",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "delivery_notification_id",
                schema: "invoicing",
                table: "invoices");
        }
    }
}
