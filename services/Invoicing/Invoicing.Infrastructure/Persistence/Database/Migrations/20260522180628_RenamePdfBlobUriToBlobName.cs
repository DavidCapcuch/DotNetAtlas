using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invoicing.Infrastructure.Persistence.Database.Migrations
{
    /// <summary>
    /// Renames <c>pdf_blob_uri → pdf_blob_name</c> on <c>invoicing.invoices</c> and
    /// <c>invoicing.credit_notes</c>, and shrinks the column to <c>varchar(1024)</c> (the
    /// Azure Blob Storage blob-name limit) from the previous <c>varchar(2048)</c> SAS-URL limit.
    /// Also drops the legacy column comments — the new <c>pdf_blob_name</c> is a canonical
    /// immutable identifier, not a presigned URL (issue #131).
    /// </summary>
    /// <remarks>
    /// EF auto-scaffold produced <c>DropColumn + AddColumn</c> which would lose data. This
    /// migration was edited to use <c>RenameColumn + AlterColumn</c> instead so that any
    /// existing rows (fixture data, etc.) retain their blob references through the migration.
    /// </remarks>
    public partial class RenamePdfBlobUriToBlobName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "pdf_blob_uri",
                schema: "invoicing",
                table: "invoices",
                newName: "pdf_blob_name");

            migrationBuilder.RenameColumn(
                name: "pdf_blob_uri",
                schema: "invoicing",
                table: "credit_notes",
                newName: "pdf_blob_name");

            migrationBuilder.AlterColumn<string>(
                name: "pdf_blob_name",
                schema: "invoicing",
                table: "invoices",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true,
                oldComment: "Presigned SAS URL to the rendered PDF in blob storage.");

            migrationBuilder.AlterColumn<string>(
                name: "pdf_blob_name",
                schema: "invoicing",
                table: "credit_notes",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true,
                oldComment: "Presigned SAS URL to the rendered credit-note PDF.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "pdf_blob_name",
                schema: "invoicing",
                table: "credit_notes",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                comment: "Presigned SAS URL to the rendered credit-note PDF.",
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "pdf_blob_name",
                schema: "invoicing",
                table: "invoices",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                comment: "Presigned SAS URL to the rendered PDF in blob storage.",
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "pdf_blob_name",
                schema: "invoicing",
                table: "credit_notes",
                newName: "pdf_blob_uri");

            migrationBuilder.RenameColumn(
                name: "pdf_blob_name",
                schema: "invoicing",
                table: "invoices",
                newName: "pdf_blob_uri");
        }
    }
}
