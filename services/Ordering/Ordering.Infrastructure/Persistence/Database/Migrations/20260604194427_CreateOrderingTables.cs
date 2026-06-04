using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ordering.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class CreateOrderingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ordering");

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "ordering",
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
                name: "orders",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Primary key (Guid v7 — time-ordered)."),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "JWT sub of the buyer who placed the order."),
                    payment_method_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Payments-side payment method reference."),
                    payment_transaction_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Payments-side transaction id after MarkPaymentCompleted (nullable pre-payment)."),
                    stock_reservation_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Inventory-side reservation id after MarkStockReserved (nullable pre-reservation)."),
                    shipping_address_street1_enc = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "PII (ADR-0011): street line 1. v1 plaintext; v2 encrypts."),
                    shipping_address_street2_enc = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "PII (ADR-0011): street line 2 (optional)."),
                    shipping_address_city_enc = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "PII (ADR-0011): city."),
                    shipping_address_state_enc = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "PII (ADR-0011): state/region (optional)."),
                    shipping_address_postal_code_enc = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "PII (ADR-0011): postal code."),
                    shipping_address_country_code_enc = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false, comment: "ISO 3166-1 alpha-2 country code."),
                    billing_address_street1_enc = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "PII (ADR-0011): street line 1. v1 plaintext; v2 encrypts."),
                    billing_address_street2_enc = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "PII (ADR-0011): street line 2 (optional)."),
                    billing_address_city_enc = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "PII (ADR-0011): city."),
                    billing_address_state_enc = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "PII (ADR-0011): state/region (optional)."),
                    billing_address_postal_code_enc = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "PII (ADR-0011): postal code."),
                    billing_address_country_code_enc = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false, comment: "ISO 3166-1 alpha-2 country code."),
                    status = table.Column<int>(type: "integer", nullable: false, comment: "Lifecycle status (Created..Delivered + Cancelled/Failed off-ramps)."),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false, comment: "Order total amount (sum of line totals)."),
                    total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, comment: "ISO 4217 currency code (uniform across all items, invariant I-9)."),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Cancellation reason (<=500 chars)."),
                    cancellation_at_status = table.Column<int>(type: "integer", nullable: true, comment: "Status the order was in when cancelled."),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when the order was cancelled."),
                    failure_error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Machine-readable error code at failure time."),
                    failure_error_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, comment: "Human-readable error message at failure time."),
                    failure_at_status = table.Column<int>(type: "integer", nullable: true, comment: "Status the order was in when it failed."),
                    failed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when the order was marked Failed."),
                    shipment_carrier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Shipping carrier identifier."),
                    shipment_tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Carrier-assigned tracking number."),
                    shipped_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when the order shipped."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the order was created (business time, frozen)."),
                    stock_reserved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when stock was reserved (nullable)."),
                    payment_completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when payment was completed (nullable)."),
                    confirmed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when the order was confirmed (nullable)."),
                    delivered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when the order was delivered (nullable)."),
                    created_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Row-level audit: created timestamp (UTC). Set by interceptor."),
                    last_modified_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Row-level audit: last-modified timestamp (UTC). Set by interceptor."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false, comment: "Optimistic concurrency token (Postgres xmin system column).")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                },
                comment: "Order aggregate — lifecycle from creation through delivery/cancellation/failure.");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, comment: "PK, Identity")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    topic_name = table.Column<string>(type: "character varying(249)", unicode: false, maxLength: 249, nullable: false, comment: "The Kafka topic where this message will be published. Set by the message producer."),
                    kafka_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, comment: "Kafka Key - typically the Aggregate ID for proper event ordering and partitioning"),
                    avro_payload = table.Column<byte[]>(type: "bytea", nullable: false, comment: "Avro-serialized domain event payload"),
                    type = table.Column<string>(type: "character varying(255)", unicode: false, maxLength: 255, nullable: false, comment: "Avro type name of the serialized event (e.g., 'FeedbackChangedEvent') for deserialization and observability"),
                    headers = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true, comment: "JSON dictionary of OpenTelemetry-standard headers for distributed tracing and metadata. Headers are automatically generated by OpenTelemetry propagators for end-to-end trace context propagation."),
                    created_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Creation timestamp (UTC).")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                },
                comment: "Outbox pattern table for storing domain events as Avro-serialized messages for reliable event publishing.");

            migrationBuilder.CreateTable(
                name: "order_items",
                schema: "ordering",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Catalog product identifier."),
                    product_sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Product SKU snapshot (frozen at order creation)."),
                    product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Product display-name snapshot (frozen at order creation)."),
                    quantity = table.Column<int>(type: "integer", nullable: false, comment: "Quantity of units (>= 1)."),
                    unit_price_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false, comment: "Per-unit price at checkout time."),
                    unit_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, comment: "ISO 4217 currency code."),
                    line_total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false, comment: "Quantity * UnitPrice (persisted to avoid recompute + map owned cleanly)."),
                    line_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, comment: "ISO 4217 currency code.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_items", x => new { x.order_id, x.ordinal });
                    table.ForeignKey(
                        name: "fk_order_items_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "ordering",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Order line items — value-object collection, no independent lifecycle.");

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_processed_at_utc",
                schema: "ordering",
                table: "inbox_messages",
                column: "processed_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_orders_buyer_id",
                schema: "ordering",
                table: "orders",
                column: "buyer_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_buyer_id_created_at_utc",
                schema: "ordering",
                table: "orders",
                columns: new[] { "buyer_id", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "order_items",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "ordering");
        }
    }
}
