using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class Add_VoidReason_To_PaymentTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "void_reason",
                schema: "payments",
                table: "payment_transactions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                comment: "Saga-supplied reason for the void (H-5 closeout; nullable until Void succeeds).");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "void_reason",
                schema: "payments",
                table: "payment_transactions");
        }
    }
}
