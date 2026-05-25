DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'ordering') THEN
        CREATE SCHEMA ordering;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS ordering."__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ordering."__EFMigrationsHistory" WHERE "migration_id" = '20260424202154_AddOrderAndOutboxInbox') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'ordering') THEN
            CREATE SCHEMA ordering;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ordering."__EFMigrationsHistory" WHERE "migration_id" = '20260424202154_AddOrderAndOutboxInbox') THEN
    CREATE TABLE ordering."InboxMessages" (
        message_id uuid NOT NULL,
        processed_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_inbox_messages PRIMARY KEY (message_id)
    );
    COMMENT ON TABLE ordering."InboxMessages" IS 'Inbox pattern table for idempotent message processing. Tracks processed messages to prevent duplicate processing.';
    COMMENT ON COLUMN ordering."InboxMessages".message_id IS 'Unique message identifier (Primary Key).';
    COMMENT ON COLUMN ordering."InboxMessages".processed_at_utc IS 'UTC timestamp when the message was processed.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ordering."__EFMigrationsHistory" WHERE "migration_id" = '20260424202154_AddOrderAndOutboxInbox') THEN
    CREATE TABLE ordering.orders (
        id uuid NOT NULL,
        buyer_id uuid NOT NULL,
        correlation_id uuid NOT NULL,
        payment_method_id uuid NOT NULL,
        payment_transaction_id uuid,
        stock_reservation_id uuid,
        shipping_address_street1_enc character varying(200) NOT NULL,
        shipping_address_street2_enc character varying(200),
        shipping_address_city_enc character varying(100) NOT NULL,
        shipping_address_state_enc character varying(100),
        shipping_address_postal_code_enc character varying(20) NOT NULL,
        shipping_address_country_code_enc character varying(2) NOT NULL,
        billing_address_street1_enc character varying(200) NOT NULL,
        billing_address_street2_enc character varying(200),
        billing_address_city_enc character varying(100) NOT NULL,
        billing_address_state_enc character varying(100),
        billing_address_postal_code_enc character varying(20) NOT NULL,
        billing_address_country_code_enc character varying(2) NOT NULL,
        status integer NOT NULL,
        total_amount numeric(19,4) NOT NULL,
        total_currency character varying(3) NOT NULL,
        cancellation_reason character varying(500),
        cancellation_at_status integer,
        cancelled_at_utc timestamp with time zone,
        failure_error_code character varying(100),
        failure_error_message character varying(1000),
        failure_at_status integer,
        failed_at_utc timestamp with time zone,
        shipment_carrier character varying(100),
        shipment_tracking_number character varying(100),
        shipped_at_utc timestamp with time zone,
        created_at_utc timestamp with time zone NOT NULL,
        stock_reserved_at_utc timestamp with time zone,
        payment_completed_at_utc timestamp with time zone,
        confirmed_at_utc timestamp with time zone,
        delivered_at_utc timestamp with time zone,
        created_utc timestamp with time zone NOT NULL,
        last_modified_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_orders PRIMARY KEY (id)
    );
    COMMENT ON TABLE ordering.orders IS 'Order aggregate — lifecycle from creation through delivery/cancellation/failure.';
    COMMENT ON COLUMN ordering.orders.id IS 'Primary key (Guid v7 — time-ordered).';
    COMMENT ON COLUMN ordering.orders.buyer_id IS 'JWT sub of the buyer who placed the order.';
    COMMENT ON COLUMN ordering.orders.correlation_id IS 'Checkout saga correlation id. Idempotency key for CreateOrderCommand.';
    COMMENT ON COLUMN ordering.orders.payment_method_id IS 'Payments-side payment method reference.';
    COMMENT ON COLUMN ordering.orders.payment_transaction_id IS 'Payments-side transaction id after MarkPaymentCompleted (nullable pre-payment).';
    COMMENT ON COLUMN ordering.orders.stock_reservation_id IS 'Inventory-side reservation id after MarkStockReserved (nullable pre-reservation).';
    COMMENT ON COLUMN ordering.orders.shipping_address_street1_enc IS 'PII (ADR-0011): street line 1. v1 plaintext; v2 encrypts.';
    COMMENT ON COLUMN ordering.orders.shipping_address_street2_enc IS 'PII (ADR-0011): street line 2 (optional).';
    COMMENT ON COLUMN ordering.orders.shipping_address_city_enc IS 'PII (ADR-0011): city.';
    COMMENT ON COLUMN ordering.orders.shipping_address_state_enc IS 'PII (ADR-0011): state/region (optional).';
    COMMENT ON COLUMN ordering.orders.shipping_address_postal_code_enc IS 'PII (ADR-0011): postal code.';
    COMMENT ON COLUMN ordering.orders.shipping_address_country_code_enc IS 'ISO 3166-1 alpha-2 country code.';
    COMMENT ON COLUMN ordering.orders.billing_address_street1_enc IS 'PII (ADR-0011): street line 1. v1 plaintext; v2 encrypts.';
    COMMENT ON COLUMN ordering.orders.billing_address_street2_enc IS 'PII (ADR-0011): street line 2 (optional).';
    COMMENT ON COLUMN ordering.orders.billing_address_city_enc IS 'PII (ADR-0011): city.';
    COMMENT ON COLUMN ordering.orders.billing_address_state_enc IS 'PII (ADR-0011): state/region (optional).';
    COMMENT ON COLUMN ordering.orders.billing_address_postal_code_enc IS 'PII (ADR-0011): postal code.';
    COMMENT ON COLUMN ordering.orders.billing_address_country_code_enc IS 'ISO 3166-1 alpha-2 country code.';
    COMMENT ON COLUMN ordering.orders.status IS 'Lifecycle status (Created..Delivered + Cancelled/Failed off-ramps).';
    COMMENT ON COLUMN ordering.orders.total_amount IS 'Order total amount (sum of line totals).';
    COMMENT ON COLUMN ordering.orders.total_currency IS 'ISO 4217 currency code (uniform across all items, invariant I-9).';
    COMMENT ON COLUMN ordering.orders.cancellation_reason IS 'Cancellation reason (<=500 chars).';
    COMMENT ON COLUMN ordering.orders.cancellation_at_status IS 'Status the order was in when cancelled.';
    COMMENT ON COLUMN ordering.orders.cancelled_at_utc IS 'UTC timestamp when the order was cancelled.';
    COMMENT ON COLUMN ordering.orders.failure_error_code IS 'Machine-readable error code at failure time.';
    COMMENT ON COLUMN ordering.orders.failure_error_message IS 'Human-readable error message at failure time.';
    COMMENT ON COLUMN ordering.orders.failure_at_status IS 'Status the order was in when it failed.';
    COMMENT ON COLUMN ordering.orders.failed_at_utc IS 'UTC timestamp when the order was marked Failed.';
    COMMENT ON COLUMN ordering.orders.shipment_carrier IS 'Shipping carrier identifier.';
    COMMENT ON COLUMN ordering.orders.shipment_tracking_number IS 'Carrier-assigned tracking number.';
    COMMENT ON COLUMN ordering.orders.shipped_at_utc IS 'UTC timestamp when the order shipped.';
    COMMENT ON COLUMN ordering.orders.created_at_utc IS 'UTC timestamp when the order was created (business time, frozen).';
    COMMENT ON COLUMN ordering.orders.stock_reserved_at_utc IS 'UTC timestamp when stock was reserved (nullable).';
    COMMENT ON COLUMN ordering.orders.payment_completed_at_utc IS 'UTC timestamp when payment was completed (nullable).';
    COMMENT ON COLUMN ordering.orders.confirmed_at_utc IS 'UTC timestamp when the order was confirmed (nullable).';
    COMMENT ON COLUMN ordering.orders.delivered_at_utc IS 'UTC timestamp when the order was delivered (nullable).';
    COMMENT ON COLUMN ordering.orders.created_utc IS 'Row-level audit: created timestamp (UTC). Set by interceptor.';
    COMMENT ON COLUMN ordering.orders.last_modified_utc IS 'Row-level audit: last-modified timestamp (UTC). Set by interceptor.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ordering."__EFMigrationsHistory" WHERE "migration_id" = '20260424202154_AddOrderAndOutboxInbox') THEN
    CREATE TABLE ordering."OutboxMessages" (
        id bigint GENERATED BY DEFAULT AS IDENTITY,
        topic_name character varying(249) NOT NULL,
        kafka_key character varying(128),
        avro_payload bytea NOT NULL,
        type character varying(255) NOT NULL,
        headers character varying(8192),
        created_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_outbox_messages PRIMARY KEY (id)
    );
    COMMENT ON TABLE ordering."OutboxMessages" IS 'Outbox pattern table for storing domain events as Avro-serialized messages for reliable event publishing.';
    COMMENT ON COLUMN ordering."OutboxMessages".id IS 'PK, Identity';
    COMMENT ON COLUMN ordering."OutboxMessages".topic_name IS 'The Kafka topic where this message will be published. Set by the message producer.';
    COMMENT ON COLUMN ordering."OutboxMessages".kafka_key IS 'Kafka Key - typically the Aggregate ID for proper event ordering and partitioning';
    COMMENT ON COLUMN ordering."OutboxMessages".avro_payload IS 'Avro-serialized domain event payload';
    COMMENT ON COLUMN ordering."OutboxMessages".type IS 'Avro type name of the serialized event (e.g., ''FeedbackChangedEvent'') for deserialization and observability';
    COMMENT ON COLUMN ordering."OutboxMessages".headers IS 'JSON dictionary of OpenTelemetry-standard headers for distributed tracing and metadata. Headers are automatically generated by OpenTelemetry propagators for end-to-end trace context propagation.';
    COMMENT ON COLUMN ordering."OutboxMessages".created_utc IS 'Creation timestamp (UTC).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ordering."__EFMigrationsHistory" WHERE "migration_id" = '20260424202154_AddOrderAndOutboxInbox') THEN
    CREATE TABLE ordering.order_items (
        order_id uuid NOT NULL,
        ordinal integer GENERATED BY DEFAULT AS IDENTITY,
        product_id uuid NOT NULL,
        product_sku character varying(64) NOT NULL,
        product_name character varying(200) NOT NULL,
        quantity integer NOT NULL,
        unit_price_amount numeric(19,4) NOT NULL,
        unit_price_currency character varying(3) NOT NULL,
        line_total_amount numeric(19,4) NOT NULL,
        line_total_currency character varying(3) NOT NULL,
        CONSTRAINT pk_order_items PRIMARY KEY (order_id, ordinal),
        CONSTRAINT fk_order_items_orders_order_id FOREIGN KEY (order_id) REFERENCES ordering.orders (id) ON DELETE CASCADE
    );
    COMMENT ON TABLE ordering.order_items IS 'Order line items — value-object collection, no independent lifecycle.';
    COMMENT ON COLUMN ordering.order_items.product_id IS 'Catalog product identifier.';
    COMMENT ON COLUMN ordering.order_items.product_sku IS 'Product SKU snapshot (frozen at order creation).';
    COMMENT ON COLUMN ordering.order_items.product_name IS 'Product display-name snapshot (frozen at order creation).';
    COMMENT ON COLUMN ordering.order_items.quantity IS 'Quantity of units (>= 1).';
    COMMENT ON COLUMN ordering.order_items.unit_price_amount IS 'Per-unit price at checkout time.';
    COMMENT ON COLUMN ordering.order_items.unit_price_currency IS 'ISO 4217 currency code.';
    COMMENT ON COLUMN ordering.order_items.line_total_amount IS 'Quantity * UnitPrice (persisted to avoid recompute + map owned cleanly).';
    COMMENT ON COLUMN ordering.order_items.line_total_currency IS 'ISO 4217 currency code.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ordering."__EFMigrationsHistory" WHERE "migration_id" = '20260424202154_AddOrderAndOutboxInbox') THEN
    CREATE INDEX "IX_InboxMessages_ProcessedAtUtc" ON ordering."InboxMessages" (processed_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ordering."__EFMigrationsHistory" WHERE "migration_id" = '20260424202154_AddOrderAndOutboxInbox') THEN
    CREATE INDEX "IX_Orders_BuyerId" ON ordering.orders (buyer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ordering."__EFMigrationsHistory" WHERE "migration_id" = '20260424202154_AddOrderAndOutboxInbox') THEN
    CREATE INDEX "IX_Orders_BuyerId_CreatedAtUtc" ON ordering.orders (buyer_id, created_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ordering."__EFMigrationsHistory" WHERE "migration_id" = '20260424202154_AddOrderAndOutboxInbox') THEN
    CREATE UNIQUE INDEX "UX_Orders_CorrelationId" ON ordering.orders (correlation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ordering."__EFMigrationsHistory" WHERE "migration_id" = '20260424202154_AddOrderAndOutboxInbox') THEN
    INSERT INTO ordering."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260424202154_AddOrderAndOutboxInbox', '10.0.8');
    END IF;
END $EF$;
