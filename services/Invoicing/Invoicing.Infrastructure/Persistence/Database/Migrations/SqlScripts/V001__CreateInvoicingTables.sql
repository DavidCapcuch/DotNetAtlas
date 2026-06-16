DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'invoicing') THEN
        CREATE SCHEMA invoicing;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS invoicing."__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'invoicing') THEN
            CREATE SCHEMA invoicing;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    CREATE TABLE invoicing.credit_note_number_allocator (
        year smallint NOT NULL,
        next_value bigint NOT NULL,
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_credit_note_number_allocator PRIMARY KEY (year),
        CONSTRAINT ck_credit_note_number_allocator_next_value CHECK (next_value >= 1)
    );
    COMMENT ON TABLE invoicing.credit_note_number_allocator IS 'Gap-free credit-note-number allocator (ADR-0018). One row per fiscal year. Locked with SELECT ... FOR UPDATE inside the issuing transaction.';
    COMMENT ON COLUMN invoicing.credit_note_number_allocator.year IS 'Fiscal year (e.g. 2026). Primary key.';
    COMMENT ON COLUMN invoicing.credit_note_number_allocator.next_value IS 'Next sequence value to hand out for this year; first issuance starts at 1.';
    COMMENT ON COLUMN invoicing.credit_note_number_allocator.updated_at IS 'Refreshed on every increment via the allocator adapter.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    CREATE TABLE invoicing.credit_notes (
        id uuid NOT NULL,
        credit_note_number character varying(14),
        original_invoice_id uuid NOT NULL,
        original_invoice_number character varying(15) NOT NULL,
        buyer_id uuid NOT NULL,
        issue_date timestamp with time zone NOT NULL,
        total_amount numeric(19,4) NOT NULL,
        total_currency character varying(3) NOT NULL,
        reason integer NOT NULL,
        pdf_blob_name character varying(1024),
        pdf_content_hash character(64),
        pdf_size_bytes bigint,
        status integer NOT NULL,
        delivered_at_utc timestamp with time zone,
        CONSTRAINT pk_credit_notes PRIMARY KEY (id)
    );
    COMMENT ON TABLE invoicing.credit_notes IS 'CreditNote aggregate — reverses a previously-issued Invoice (sign-flipped lines).';
    COMMENT ON COLUMN invoicing.credit_notes.id IS 'Primary key (Guid v7).';
    COMMENT ON COLUMN invoicing.credit_notes.credit_note_number IS 'Gap-free credit-note number, format CN-YYYY-NNNNNN.';
    COMMENT ON COLUMN invoicing.credit_notes.original_invoice_id IS 'Identifier of the Invoice this credit note reverses.';
    COMMENT ON COLUMN invoicing.credit_notes.original_invoice_number IS 'Snapshot of the original Invoice''s number for PDF rendering and reconciliation.';
    COMMENT ON COLUMN invoicing.credit_notes.buyer_id IS 'Buyer of the original invoice (and therefore the credit note).';
    COMMENT ON COLUMN invoicing.credit_notes.issue_date IS 'UTC timestamp when the credit note was issued (number stamped + PDF stored).';
    COMMENT ON COLUMN invoicing.credit_notes.reason IS 'CreditNoteReason (v1: OrderCancelled).';
    COMMENT ON COLUMN invoicing.credit_notes.pdf_content_hash IS 'SHA-256 of the PDF bytes, lowercase hex (64 chars).';
    COMMENT ON COLUMN invoicing.credit_notes.pdf_size_bytes IS 'PDF size in bytes (>0).';
    COMMENT ON COLUMN invoicing.credit_notes.status IS 'Credit-note lifecycle status (Issued|Delivered|Archived).';
    COMMENT ON COLUMN invoicing.credit_notes.delivered_at_utc IS 'UTC timestamp when the credit note transitioned to Delivered (nullable).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    CREATE TABLE invoicing.inbox_messages (
        message_id uuid NOT NULL,
        processed_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_inbox_messages PRIMARY KEY (message_id)
    );
    COMMENT ON TABLE invoicing.inbox_messages IS 'Inbox pattern table for idempotent message processing. Tracks processed messages to prevent duplicate processing.';
    COMMENT ON COLUMN invoicing.inbox_messages.message_id IS 'Unique message identifier (Primary Key).';
    COMMENT ON COLUMN invoicing.inbox_messages.processed_at_utc IS 'UTC timestamp when the message was processed.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    CREATE TABLE invoicing.invoice_number_allocator (
        year smallint NOT NULL,
        next_value bigint NOT NULL,
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_invoice_number_allocator PRIMARY KEY (year),
        CONSTRAINT ck_invoice_number_allocator_next_value CHECK (next_value >= 1)
    );
    COMMENT ON TABLE invoicing.invoice_number_allocator IS 'Gap-free invoice-number allocator (ADR-0018). One row per fiscal year. Locked with SELECT ... FOR UPDATE inside the issuing transaction; rollback releases the lock without incrementing next_value.';
    COMMENT ON COLUMN invoicing.invoice_number_allocator.year IS 'Fiscal year (e.g. 2026). Primary key.';
    COMMENT ON COLUMN invoicing.invoice_number_allocator.next_value IS 'Next sequence value to hand out for this year; first issuance starts at 1.';
    COMMENT ON COLUMN invoicing.invoice_number_allocator.updated_at IS 'Refreshed on every increment via the allocator adapter.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    CREATE TABLE invoicing.invoices (
        id uuid NOT NULL,
        invoice_number character varying(15),
        buyer_id uuid NOT NULL,
        order_id uuid NOT NULL,
        payment_id uuid NOT NULL,
        issue_date timestamp with time zone NOT NULL,
        billing_address_street1_enc character varying(200) NOT NULL,
        billing_address_street2_enc character varying(200),
        billing_address_city_enc character varying(100) NOT NULL,
        billing_address_state_enc character varying(100),
        billing_address_postal_code_enc character varying(20) NOT NULL,
        billing_address_country_code_enc character varying(2) NOT NULL,
        subtotal_amount numeric(19,4) NOT NULL,
        subtotal_currency character varying(3) NOT NULL,
        total_amount numeric(19,4) NOT NULL,
        total_currency character varying(3) NOT NULL,
        pdf_blob_name character varying(1024),
        pdf_content_hash character(64),
        pdf_size_bytes bigint,
        delivery_channel integer NOT NULL,
        status integer NOT NULL,
        cancelled_at_utc timestamp with time zone,
        cancellation_reason integer,
        cancellation_credit_note_id uuid,
        delivered_at_utc timestamp with time zone,
        CONSTRAINT pk_invoices PRIMARY KEY (id)
    );
    COMMENT ON TABLE invoicing.invoices IS 'Invoice aggregate — fiscal record issued after order confirmation + payment capture.';
    COMMENT ON COLUMN invoicing.invoices.id IS 'Primary key (Guid v7 — time-ordered).';
    COMMENT ON COLUMN invoicing.invoices.invoice_number IS 'Gap-free invoice number, format INV-YYYY-NNNNNN. Null while Draft.';
    COMMENT ON COLUMN invoicing.invoices.buyer_id IS 'JWT sub of the buyer the invoice is issued to.';
    COMMENT ON COLUMN invoicing.invoices.order_id IS 'Reference to the Ordering Order the invoice settles.';
    COMMENT ON COLUMN invoicing.invoices.payment_id IS 'Reference to the Payments transaction the invoice settles.';
    COMMENT ON COLUMN invoicing.invoices.issue_date IS 'UTC timestamp when the invoice transitioned to Issued.';
    COMMENT ON COLUMN invoicing.invoices.billing_address_street1_enc IS 'PII (ADR-0011): street line 1. v1 plaintext; v2 encrypts.';
    COMMENT ON COLUMN invoicing.invoices.billing_address_street2_enc IS 'PII (ADR-0011): street line 2 (optional).';
    COMMENT ON COLUMN invoicing.invoices.billing_address_city_enc IS 'PII (ADR-0011): city.';
    COMMENT ON COLUMN invoicing.invoices.billing_address_state_enc IS 'PII (ADR-0011): state/region (optional).';
    COMMENT ON COLUMN invoicing.invoices.billing_address_postal_code_enc IS 'PII (ADR-0011): postal code.';
    COMMENT ON COLUMN invoicing.invoices.billing_address_country_code_enc IS 'ISO 3166-1 alpha-2 country code.';
    COMMENT ON COLUMN invoicing.invoices.pdf_content_hash IS 'SHA-256 of the PDF bytes, lowercase hex (64 chars).';
    COMMENT ON COLUMN invoicing.invoices.pdf_size_bytes IS 'PDF size in bytes (>0).';
    COMMENT ON COLUMN invoicing.invoices.delivery_channel IS 'Intended delivery channel (None|Email|TaxAuthorityWebhook).';
    COMMENT ON COLUMN invoicing.invoices.status IS 'Invoice lifecycle status (Draft|Issued|Delivered|Archived|Cancelled).';
    COMMENT ON COLUMN invoicing.invoices.cancelled_at_utc IS 'UTC timestamp when the invoice transitioned to Cancelled.';
    COMMENT ON COLUMN invoicing.invoices.cancellation_reason IS 'CreditNoteReason explaining why the invoice was cancelled.';
    COMMENT ON COLUMN invoicing.invoices.cancellation_credit_note_id IS 'Identifier of the reversing CreditNote (Invoice invariant I-6).';
    COMMENT ON COLUMN invoicing.invoices.delivered_at_utc IS 'UTC timestamp when the invoice transitioned to Delivered (nullable).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    CREATE TABLE invoicing.outbox_messages (
        id bigint GENERATED BY DEFAULT AS IDENTITY,
        topic_name character varying(249) NOT NULL,
        kafka_key character varying(128),
        avro_payload bytea NOT NULL,
        type character varying(255) NOT NULL,
        headers character varying(8192),
        created_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_outbox_messages PRIMARY KEY (id)
    );
    COMMENT ON TABLE invoicing.outbox_messages IS 'Outbox pattern table for storing domain events as Avro-serialized messages for reliable event publishing.';
    COMMENT ON COLUMN invoicing.outbox_messages.id IS 'PK, Identity';
    COMMENT ON COLUMN invoicing.outbox_messages.topic_name IS 'The Kafka topic where this message will be published. Set by the message producer.';
    COMMENT ON COLUMN invoicing.outbox_messages.kafka_key IS 'Kafka Key - typically the Aggregate ID for proper event ordering and partitioning';
    COMMENT ON COLUMN invoicing.outbox_messages.avro_payload IS 'Avro-serialized domain event payload';
    COMMENT ON COLUMN invoicing.outbox_messages.type IS 'Avro type name of the serialized event (e.g., ''OrderConfirmedEvent'') for deserialization and observability';
    COMMENT ON COLUMN invoicing.outbox_messages.headers IS 'JSON dictionary of OpenTelemetry-standard headers for distributed tracing and metadata. Headers are automatically generated by OpenTelemetry propagators for end-to-end trace context propagation.';
    COMMENT ON COLUMN invoicing.outbox_messages.created_utc IS 'Creation timestamp (UTC).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    CREATE TABLE invoicing.pending_credit_notes (
        order_id uuid NOT NULL,
        payment_id uuid,
        buyer_id uuid,
        order_payload jsonb,
        payment_payload jsonb,
        first_seen_at_utc timestamp with time zone NOT NULL,
        completed_at_utc timestamp with time zone,
        issued_credit_note_id uuid,
        CONSTRAINT pk_pending_credit_notes PRIMARY KEY (order_id)
    );
    COMMENT ON TABLE invoicing.pending_credit_notes IS 'Async-enrichment buffer: collects OrderCancelledEvent + PaymentRefundedEvent halves keyed on OrderId until IssueCreditNoteCommandHandler converts the converged row into a CreditNote aggregate.';
    COMMENT ON COLUMN invoicing.pending_credit_notes.order_id IS 'OrderCancelledEvent.OrderId; the cross-BC convergence key. Primary key.';
    COMMENT ON COLUMN invoicing.pending_credit_notes.payment_id IS 'PaymentRefundedEvent.PaymentTransactionId — the original captured payment, not the refund txn id.';
    COMMENT ON COLUMN invoicing.pending_credit_notes.buyer_id IS 'OrderCancelledEvent.BuyerId; the outbox publisher uses this as the partition key.';
    COMMENT ON COLUMN invoicing.pending_credit_notes.order_payload IS 'PII: full OrderCancelledEvent serialised to JSON for issuance-time hydration.';
    COMMENT ON COLUMN invoicing.pending_credit_notes.payment_payload IS 'Full PaymentRefundedEvent serialised to JSON for issuance-time hydration.';
    COMMENT ON COLUMN invoicing.pending_credit_notes.first_seen_at_utc IS 'Wall-clock at first observation; never overwritten.';
    COMMENT ON COLUMN invoicing.pending_credit_notes.completed_at_utc IS 'Set when both halves are present.';
    COMMENT ON COLUMN invoicing.pending_credit_notes.issued_credit_note_id IS 'Set by IssueCreditNoteCommandHandler atomically with the CreditNote aggregate insert.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    CREATE TABLE invoicing.pending_invoices (
        order_id uuid NOT NULL,
        payment_id uuid,
        buyer_id uuid,
        order_payload jsonb,
        payment_payload jsonb,
        first_seen_at_utc timestamp with time zone NOT NULL,
        completed_at_utc timestamp with time zone,
        issued_invoice_id uuid,
        CONSTRAINT pk_pending_invoices PRIMARY KEY (order_id)
    );
    COMMENT ON TABLE invoicing.pending_invoices IS 'Async-enrichment buffer: collects OrderConfirmedEvent + PaymentCapturedEvent halves keyed on OrderId until IssueInvoiceCommandHandler converts the converged row into an Invoice aggregate.';
    COMMENT ON COLUMN invoicing.pending_invoices.order_id IS 'OrderConfirmedEvent.OrderId; the cross-BC convergence key. Primary key.';
    COMMENT ON COLUMN invoicing.pending_invoices.payment_id IS 'PaymentCapturedEvent.PaymentTransactionId; null until the payment half arrives.';
    COMMENT ON COLUMN invoicing.pending_invoices.buyer_id IS 'OrderConfirmedEvent.BuyerId; the outbox publisher uses this as the partition key on invoicing.invoices.';
    COMMENT ON COLUMN invoicing.pending_invoices.order_payload IS 'PII: full OrderConfirmedEvent serialised to JSON for issuance-time hydration.';
    COMMENT ON COLUMN invoicing.pending_invoices.payment_payload IS 'PII: full PaymentCapturedEvent serialised to JSON for issuance-time hydration.';
    COMMENT ON COLUMN invoicing.pending_invoices.first_seen_at_utc IS 'Wall-clock at first observation; never overwritten on subsequent updates.';
    COMMENT ON COLUMN invoicing.pending_invoices.completed_at_utc IS 'Set when both halves are present.';
    COMMENT ON COLUMN invoicing.pending_invoices.issued_invoice_id IS 'Set by IssueInvoiceCommandHandler atomically with the Invoice aggregate insert.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    CREATE TABLE invoicing.credit_note_lines (
        line_number integer GENERATED BY DEFAULT AS IDENTITY,
        credit_note_id uuid NOT NULL,
        sku character varying(64) NOT NULL,
        description character varying(500) NOT NULL,
        quantity integer NOT NULL,
        unit_price_amount numeric(19,4) NOT NULL,
        unit_price_currency character varying(3) NOT NULL,
        line_total_amount numeric(19,4) NOT NULL,
        line_total_currency character varying(3) NOT NULL,
        vat_rate_percentage numeric(5,2) NOT NULL,
        CONSTRAINT pk_credit_note_lines PRIMARY KEY (credit_note_id, line_number),
        CONSTRAINT fk_credit_note_lines_credit_notes_credit_note_id FOREIGN KEY (credit_note_id) REFERENCES invoicing.credit_notes (id) ON DELETE CASCADE
    );
    COMMENT ON TABLE invoicing.credit_note_lines IS 'CreditNoteLine items — backward-looking corrections of the source invoice''s lines.';
    COMMENT ON COLUMN invoicing.credit_note_lines.line_number IS 'Position on the credit note (1-based; mirrors the original invoice line''s number).';
    COMMENT ON COLUMN invoicing.credit_note_lines.sku IS 'Catalog SKU snapshot from the reversed invoice line.';
    COMMENT ON COLUMN invoicing.credit_note_lines.description IS 'Human-readable line description (copied from the source invoice line).';
    COMMENT ON COLUMN invoicing.credit_note_lines.quantity IS 'Units being credited (>= 1).';
    COMMENT ON COLUMN invoicing.credit_note_lines.vat_rate_percentage IS 'VAT rate from the reversed invoice line, in [0, 100].';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    CREATE TABLE invoicing.invoice_lines (
        line_number integer GENERATED BY DEFAULT AS IDENTITY,
        invoice_id uuid NOT NULL,
        sku character varying(64) NOT NULL,
        description character varying(500) NOT NULL,
        quantity integer NOT NULL,
        unit_price_amount numeric(19,4) NOT NULL,
        unit_price_currency character varying(3) NOT NULL,
        line_total_amount numeric(19,4) NOT NULL,
        line_total_currency character varying(3) NOT NULL,
        vat_rate_percentage numeric(5,2) NOT NULL,
        CONSTRAINT pk_invoice_lines PRIMARY KEY (invoice_id, line_number),
        CONSTRAINT fk_invoice_lines_invoices_invoice_id FOREIGN KEY (invoice_id) REFERENCES invoicing.invoices (id) ON DELETE CASCADE
    );
    COMMENT ON TABLE invoicing.invoice_lines IS 'Invoice line items — frozen at issuance per Invoice invariant I-2.';
    COMMENT ON COLUMN invoicing.invoice_lines.line_number IS 'Position on the document (1-based).';
    COMMENT ON COLUMN invoicing.invoice_lines.sku IS 'Catalog SKU snapshot at issuance.';
    COMMENT ON COLUMN invoicing.invoice_lines.description IS 'Human-readable line description.';
    COMMENT ON COLUMN invoicing.invoice_lines.quantity IS 'Units on the line (>= 1).';
    COMMENT ON COLUMN invoicing.invoice_lines.vat_rate_percentage IS 'Applicable VAT rate, in [0, 100].';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    CREATE TABLE invoicing.invoice_vat_lines (
        invoice_id uuid NOT NULL,
        ordinal integer GENERATED BY DEFAULT AS IDENTITY,
        rate_percentage numeric(5,2) NOT NULL,
        base_amount numeric(19,4) NOT NULL,
        base_currency character varying(3) NOT NULL,
        amount_amount numeric(19,4) NOT NULL,
        amount_currency character varying(3) NOT NULL,
        CONSTRAINT pk_invoice_vat_lines PRIMARY KEY (invoice_id, ordinal),
        CONSTRAINT fk_invoice_vat_lines_invoices_invoice_id FOREIGN KEY (invoice_id) REFERENCES invoicing.invoices (id) ON DELETE CASCADE
    );
    COMMENT ON TABLE invoicing.invoice_vat_lines IS 'Per-rate VAT breakdown for the invoice. Empty when every line is at 0%.';
    COMMENT ON COLUMN invoicing.invoice_vat_lines.rate_percentage IS 'VAT rate percentage in [0, 100], 2 decimals.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    CREATE INDEX ix_credit_notes_buyer_id ON invoicing.credit_notes (buyer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    CREATE UNIQUE INDEX ux_credit_notes_credit_note_number ON invoicing.credit_notes (credit_note_number) WHERE credit_note_number IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    CREATE UNIQUE INDEX ux_credit_notes_original_invoice_id ON invoicing.credit_notes (original_invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    CREATE INDEX ix_inbox_messages_processed_at_utc ON invoicing.inbox_messages (processed_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    CREATE INDEX ix_invoices_buyer_id ON invoicing.invoices (buyer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    CREATE UNIQUE INDEX ux_invoices_invoice_number ON invoicing.invoices (invoice_number) WHERE invoice_number IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    CREATE UNIQUE INDEX ux_invoices_order_id ON invoicing.invoices (order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    CREATE INDEX ix_pending_credit_notes_ready ON invoicing.pending_credit_notes (completed_at_utc, issued_credit_note_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    CREATE INDEX ix_pending_invoices_ready ON invoicing.pending_invoices (completed_at_utc, issued_invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604194521_CreateInvoicingTables') THEN
    INSERT INTO invoicing."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260604194521_CreateInvoicingTables', '10.0.8');
    END IF;
END $EF$;
