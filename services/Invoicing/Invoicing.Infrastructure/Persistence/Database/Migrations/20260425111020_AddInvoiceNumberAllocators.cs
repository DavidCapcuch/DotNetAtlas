using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invoicing.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceNumberAllocators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "invoicing");

            migrationBuilder.CreateTable(
                name: "credit_note_number_allocator",
                schema: "invoicing",
                columns: table => new
                {
                    year = table.Column<short>(type: "smallint", nullable: false, comment: "Fiscal year (e.g. 2026). Primary key."),
                    next_value = table.Column<long>(type: "bigint", nullable: false, comment: "Next sequence value to hand out for this year; first issuance starts at 1."),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()", comment: "Refreshed on every increment via the allocator adapter.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_note_number_allocator", x => x.year);
                    table.CheckConstraint("ck_credit_note_number_allocator_next_value", "next_value >= 1");
                },
                comment: "Gap-free credit-note-number allocator (ADR-0018). One row per fiscal year. Locked with SELECT ... FOR UPDATE inside the issuing transaction.");

            migrationBuilder.CreateTable(
                name: "invoice_number_allocator",
                schema: "invoicing",
                columns: table => new
                {
                    year = table.Column<short>(type: "smallint", nullable: false, comment: "Fiscal year (e.g. 2026). Primary key."),
                    next_value = table.Column<long>(type: "bigint", nullable: false, comment: "Next sequence value to hand out for this year; first issuance starts at 1."),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()", comment: "Refreshed on every increment via the allocator adapter.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_number_allocator", x => x.year);
                    table.CheckConstraint("ck_invoice_number_allocator_next_value", "next_value >= 1");
                },
                comment: "Gap-free invoice-number allocator (ADR-0018). One row per fiscal year. Locked with SELECT ... FOR UPDATE inside the issuing transaction; rollback releases the lock without incrementing next_value.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_note_number_allocator",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "invoice_number_allocator",
                schema: "invoicing");
        }
    }
}
