using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Invoicing.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class SplitCreditNoteLineFromInvoiceLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_credit_note_lines",
                schema: "invoicing",
                table: "credit_note_lines");

            migrationBuilder.AlterTable(
                name: "credit_note_lines",
                schema: "invoicing",
                comment: "CreditNoteLine items — backward-looking corrections of the source invoice's lines.",
                oldComment: "CreditNote line items — sign-flipped copy of the original Invoice's lines.");

            migrationBuilder.AlterColumn<decimal>(
                name: "vat_rate_percentage",
                schema: "invoicing",
                table: "credit_note_lines",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                comment: "VAT rate from the reversed invoice line, in [0, 100].",
                oldClrType: typeof(decimal),
                oldType: "numeric(5,2)",
                oldPrecision: 5,
                oldScale: 2,
                oldComment: "Applicable VAT rate, in [0, 100].");

            migrationBuilder.AlterColumn<string>(
                name: "sku",
                schema: "invoicing",
                table: "credit_note_lines",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "Catalog SKU snapshot from the reversed invoice line.",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "Catalog SKU snapshot at issuance.");

            migrationBuilder.AlterColumn<int>(
                name: "quantity",
                schema: "invoicing",
                table: "credit_note_lines",
                type: "integer",
                nullable: false,
                comment: "Units being credited (>= 1).",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Units on the line (>= 1).");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                schema: "invoicing",
                table: "credit_note_lines",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "Human-readable line description (copied from the source invoice line).",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "Human-readable line description.");

            migrationBuilder.AlterColumn<int>(
                name: "line_number",
                schema: "invoicing",
                table: "credit_note_lines",
                type: "integer",
                nullable: false,
                comment: "Position on the credit note (1-based; mirrors the original invoice line's number).",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Position on the document (1-based).")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "pk_credit_note_lines",
                schema: "invoicing",
                table: "credit_note_lines",
                columns: new[] { "credit_note_id", "line_number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_credit_note_lines",
                schema: "invoicing",
                table: "credit_note_lines");

            migrationBuilder.AlterTable(
                name: "credit_note_lines",
                schema: "invoicing",
                comment: "CreditNote line items — sign-flipped copy of the original Invoice's lines.",
                oldComment: "CreditNoteLine items — backward-looking corrections of the source invoice's lines.");

            migrationBuilder.AlterColumn<decimal>(
                name: "vat_rate_percentage",
                schema: "invoicing",
                table: "credit_note_lines",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                comment: "Applicable VAT rate, in [0, 100].",
                oldClrType: typeof(decimal),
                oldType: "numeric(5,2)",
                oldPrecision: 5,
                oldScale: 2,
                oldComment: "VAT rate from the reversed invoice line, in [0, 100].");

            migrationBuilder.AlterColumn<string>(
                name: "sku",
                schema: "invoicing",
                table: "credit_note_lines",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "Catalog SKU snapshot at issuance.",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "Catalog SKU snapshot from the reversed invoice line.");

            migrationBuilder.AlterColumn<int>(
                name: "quantity",
                schema: "invoicing",
                table: "credit_note_lines",
                type: "integer",
                nullable: false,
                comment: "Units on the line (>= 1).",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Units being credited (>= 1).");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                schema: "invoicing",
                table: "credit_note_lines",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "Human-readable line description.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "Human-readable line description (copied from the source invoice line).");

            migrationBuilder.AlterColumn<int>(
                name: "line_number",
                schema: "invoicing",
                table: "credit_note_lines",
                type: "integer",
                nullable: false,
                comment: "Position on the document (1-based).",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Position on the credit note (1-based; mirrors the original invoice line's number).")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "PK_credit_note_lines",
                schema: "invoicing",
                table: "credit_note_lines",
                columns: new[] { "credit_note_id", "line_number" });
        }
    }
}
