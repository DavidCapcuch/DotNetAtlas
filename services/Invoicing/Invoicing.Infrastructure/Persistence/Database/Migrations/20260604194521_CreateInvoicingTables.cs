using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Invoicing.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class CreateInvoicingTables : Migration
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
                name: "credit_notes",
                schema: "invoicing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Primary key (Guid v7)."),
                    credit_note_number = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true, comment: "Gap-free credit-note number, format CN-YYYY-NNNNNN."),
                    original_invoice_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Identifier of the Invoice this credit note reverses."),
                    original_invoice_number = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false, comment: "Snapshot of the original Invoice's number for PDF rendering and reconciliation."),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Buyer of the original invoice (and therefore the credit note)."),
                    issue_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the credit note was issued (number stamped + PDF stored)."),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    reason = table.Column<int>(type: "integer", nullable: false, comment: "CreditNoteReason (v1: OrderCancelled)."),
                    pdf_blob_name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    pdf_content_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true, comment: "SHA-256 of the PDF bytes, lowercase hex (64 chars)."),
                    pdf_size_bytes = table.Column<long>(type: "bigint", nullable: true, comment: "PDF size in bytes (>0)."),
                    status = table.Column<int>(type: "integer", nullable: false, comment: "Credit-note lifecycle status (Issued|Delivered|Archived)."),
                    delivered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when the credit note transitioned to Delivered (nullable)."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false, comment: "Optimistic concurrency token (Postgres xmin system column).")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_notes", x => x.id);
                },
                comment: "CreditNote aggregate — reverses a previously-issued Invoice (sign-flipped lines).");

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "invoicing",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Unique message identifier (Primary Key)."),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the message was processed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => x.message_id);
                },
                comment: "Inbox pattern table for idempotent message processing. Tracks processed messages to prevent duplicate processing.");

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

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "invoicing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Primary key (Guid v7 — time-ordered)."),
                    invoice_number = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true, comment: "Gap-free invoice number, format INV-YYYY-NNNNNN. Null while Draft."),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "JWT sub of the buyer the invoice is issued to."),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Reference to the Ordering Order the invoice settles."),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Reference to the Payments transaction the invoice settles."),
                    issue_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the invoice transitioned to Issued."),
                    billing_address_street1_enc = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "PII (ADR-0011): street line 1. v1 plaintext; v2 encrypts."),
                    billing_address_street2_enc = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "PII (ADR-0011): street line 2 (optional)."),
                    billing_address_city_enc = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "PII (ADR-0011): city."),
                    billing_address_state_enc = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "PII (ADR-0011): state/region (optional)."),
                    billing_address_postal_code_enc = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "PII (ADR-0011): postal code."),
                    billing_address_country_code_enc = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false, comment: "ISO 3166-1 alpha-2 country code."),
                    subtotal_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    subtotal_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    pdf_blob_name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    pdf_content_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true, comment: "SHA-256 of the PDF bytes, lowercase hex (64 chars)."),
                    pdf_size_bytes = table.Column<long>(type: "bigint", nullable: true, comment: "PDF size in bytes (>0)."),
                    delivery_channel = table.Column<int>(type: "integer", nullable: false, comment: "Intended delivery channel (None|Email|TaxAuthorityWebhook)."),
                    status = table.Column<int>(type: "integer", nullable: false, comment: "Invoice lifecycle status (Draft|Issued|Delivered|Archived|Cancelled)."),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when the invoice transitioned to Cancelled."),
                    cancellation_reason = table.Column<int>(type: "integer", nullable: true, comment: "CreditNoteReason explaining why the invoice was cancelled."),
                    cancellation_credit_note_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Identifier of the reversing CreditNote (Invoice invariant I-6)."),
                    delivered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when the invoice transitioned to Delivered (nullable)."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false, comment: "Optimistic concurrency token (Postgres xmin system column).")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                },
                comment: "Invoice aggregate — fiscal record issued after order confirmation + payment capture.");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "invoicing",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, comment: "PK, Identity")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    topic_name = table.Column<string>(type: "character varying(249)", unicode: false, maxLength: 249, nullable: false, comment: "The Kafka topic where this message will be published. Set by the message producer."),
                    kafka_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, comment: "Kafka Key - typically the Aggregate ID for proper event ordering and partitioning"),
                    avro_payload = table.Column<byte[]>(type: "bytea", nullable: false, comment: "Avro-serialized domain event payload"),
                    type = table.Column<string>(type: "character varying(255)", unicode: false, maxLength: 255, nullable: false, comment: "Avro type name of the serialized event (e.g., 'OrderConfirmedEvent') for deserialization and observability"),
                    headers = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true, comment: "JSON dictionary of OpenTelemetry-standard headers for distributed tracing and metadata. Headers are automatically generated by OpenTelemetry propagators for end-to-end trace context propagation."),
                    created_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Creation timestamp (UTC).")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                },
                comment: "Outbox pattern table for storing domain events as Avro-serialized messages for reliable event publishing.");

            migrationBuilder.CreateTable(
                name: "pending_credit_notes",
                schema: "invoicing",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "OrderCancelledEvent.OrderId; the cross-BC convergence key. Primary key."),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "PaymentRefundedEvent.PaymentTransactionId — the original captured payment, not the refund txn id."),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "OrderCancelledEvent.BuyerId; the outbox publisher uses this as the partition key."),
                    order_payload = table.Column<string>(type: "jsonb", nullable: true, comment: "PII: full OrderCancelledEvent serialised to JSON for issuance-time hydration."),
                    payment_payload = table.Column<string>(type: "jsonb", nullable: true, comment: "Full PaymentRefundedEvent serialised to JSON for issuance-time hydration."),
                    first_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Wall-clock at first observation; never overwritten."),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Set when both halves are present."),
                    issued_credit_note_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Set by IssueCreditNoteCommandHandler atomically with the CreditNote aggregate insert.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_credit_notes", x => x.order_id);
                },
                comment: "Async-enrichment buffer: collects OrderCancelledEvent + PaymentRefundedEvent halves keyed on OrderId until IssueCreditNoteCommandHandler converts the converged row into a CreditNote aggregate.");

            migrationBuilder.CreateTable(
                name: "pending_invoices",
                schema: "invoicing",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "OrderConfirmedEvent.OrderId; the cross-BC convergence key. Primary key."),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "PaymentCapturedEvent.PaymentTransactionId; null until the payment half arrives."),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "OrderConfirmedEvent.BuyerId; the outbox publisher uses this as the partition key on invoicing.invoices."),
                    order_payload = table.Column<string>(type: "jsonb", nullable: true, comment: "PII: full OrderConfirmedEvent serialised to JSON for issuance-time hydration."),
                    payment_payload = table.Column<string>(type: "jsonb", nullable: true, comment: "PII: full PaymentCapturedEvent serialised to JSON for issuance-time hydration."),
                    first_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Wall-clock at first observation; never overwritten on subsequent updates."),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Set when both halves are present."),
                    issued_invoice_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Set by IssueInvoiceCommandHandler atomically with the Invoice aggregate insert.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_invoices", x => x.order_id);
                },
                comment: "Async-enrichment buffer: collects OrderConfirmedEvent + PaymentCapturedEvent halves keyed on OrderId until IssueInvoiceCommandHandler converts the converged row into an Invoice aggregate.");

            migrationBuilder.CreateTable(
                name: "credit_note_lines",
                schema: "invoicing",
                columns: table => new
                {
                    line_number = table.Column<int>(type: "integer", nullable: false, comment: "Position on the credit note (1-based; mirrors the original invoice line's number).")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    credit_note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Catalog SKU snapshot from the reversed invoice line."),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "Human-readable line description (copied from the source invoice line)."),
                    quantity = table.Column<int>(type: "integer", nullable: false, comment: "Units being credited (>= 1)."),
                    unit_price_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    unit_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    line_total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    line_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    vat_rate_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, comment: "VAT rate from the reversed invoice line, in [0, 100].")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_note_lines", x => new { x.credit_note_id, x.line_number });
                    table.ForeignKey(
                        name: "fk_credit_note_lines_credit_notes_credit_note_id",
                        column: x => x.credit_note_id,
                        principalSchema: "invoicing",
                        principalTable: "credit_notes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "CreditNoteLine items — backward-looking corrections of the source invoice's lines.");

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                schema: "invoicing",
                columns: table => new
                {
                    line_number = table.Column<int>(type: "integer", nullable: false, comment: "Position on the document (1-based).")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Catalog SKU snapshot at issuance."),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "Human-readable line description."),
                    quantity = table.Column<int>(type: "integer", nullable: false, comment: "Units on the line (>= 1)."),
                    unit_price_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    unit_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    line_total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    line_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    vat_rate_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, comment: "Applicable VAT rate, in [0, 100].")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_lines", x => new { x.invoice_id, x.line_number });
                    table.ForeignKey(
                        name: "fk_invoice_lines_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "invoicing",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Invoice line items — frozen at issuance per Invoice invariant I-2.");

            migrationBuilder.CreateTable(
                name: "invoice_vat_lines",
                schema: "invoicing",
                columns: table => new
                {
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    rate_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, comment: "VAT rate percentage in [0, 100], 2 decimals."),
                    base_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    base_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_vat_lines", x => new { x.invoice_id, x.ordinal });
                    table.ForeignKey(
                        name: "fk_invoice_vat_lines_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "invoicing",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Per-rate VAT breakdown for the invoice. Empty when every line is at 0%.");

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_buyer_id",
                schema: "invoicing",
                table: "credit_notes",
                column: "buyer_id");

            migrationBuilder.CreateIndex(
                name: "ux_credit_notes_credit_note_number",
                schema: "invoicing",
                table: "credit_notes",
                column: "credit_note_number",
                unique: true,
                filter: "credit_note_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_credit_notes_original_invoice_id",
                schema: "invoicing",
                table: "credit_notes",
                column: "original_invoice_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_processed_at_utc",
                schema: "invoicing",
                table: "inbox_messages",
                column: "processed_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_buyer_id",
                schema: "invoicing",
                table: "invoices",
                column: "buyer_id");

            migrationBuilder.CreateIndex(
                name: "ux_invoices_invoice_number",
                schema: "invoicing",
                table: "invoices",
                column: "invoice_number",
                unique: true,
                filter: "invoice_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_invoices_order_id",
                schema: "invoicing",
                table: "invoices",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pending_credit_notes_ready",
                schema: "invoicing",
                table: "pending_credit_notes",
                columns: new[] { "completed_at_utc", "issued_credit_note_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pending_invoices_ready",
                schema: "invoicing",
                table: "pending_invoices",
                columns: new[] { "completed_at_utc", "issued_invoice_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_note_lines",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "credit_note_number_allocator",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "invoice_lines",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "invoice_number_allocator",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "invoice_vat_lines",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "pending_credit_notes",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "pending_invoices",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "credit_notes",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "invoicing");
        }
    }
}
