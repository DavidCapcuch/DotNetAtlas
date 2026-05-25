
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260508181028_AddInvoicesCreditNotesAndOutbox') THEN
    CREATE TABLE invoicing.credit_notes (
        id uuid NOT NULL,
        credit_note_number character varying(14),
        original_invoice_id uuid NOT NULL,
        original_invoice_number character varying(15) NOT NULL,
        buyer_id uuid NOT NULL,
        correlation_id uuid NOT NULL,
        issue_date timestamp with time zone NOT NULL,
        total_amount numeric(19,4) NOT NULL,
        total_currency character varying(3) NOT NULL,
        reason integer NOT NULL,
        pdf_blob_uri character varying(2048),
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
    COMMENT ON COLUMN invoicing.credit_notes.correlation_id IS 'Cancellation flow correlation id; used as M7 idempotency key.';
    COMMENT ON COLUMN invoicing.credit_notes.issue_date IS 'UTC timestamp when the credit note was issued (number stamped + PDF stored).';
    COMMENT ON COLUMN invoicing.credit_notes.reason IS 'CreditNoteReason (v1: OrderCancelled).';
    COMMENT ON COLUMN invoicing.credit_notes.pdf_blob_uri IS 'Presigned SAS URL to the rendered credit-note PDF.';
    COMMENT ON COLUMN invoicing.credit_notes.pdf_content_hash IS 'SHA-256 of the PDF bytes, lowercase hex (64 chars).';
    COMMENT ON COLUMN invoicing.credit_notes.pdf_size_bytes IS 'PDF size in bytes (>0).';
    COMMENT ON COLUMN invoicing.credit_notes.status IS 'Credit-note lifecycle status (Issued|Delivered|Archived).';
    COMMENT ON COLUMN invoicing.credit_notes.delivered_at_utc IS 'UTC timestamp when the credit note transitioned to Delivered (nullable).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260508181028_AddInvoicesCreditNotesAndOutbox') THEN
    CREATE TABLE invoicing.invoices (
        id uuid NOT NULL,
        invoice_number character varying(15),
        buyer_id uuid NOT NULL,
        order_id uuid NOT NULL,
        payment_id uuid NOT NULL,
        correlation_id uuid NOT NULL,
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
        pdf_blob_uri character varying(2048),
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
    COMMENT ON COLUMN invoicing.invoices.correlation_id IS 'Checkout saga correlation id (passed through from Order + Payment).';
    COMMENT ON COLUMN invoicing.invoices.issue_date IS 'UTC timestamp when the invoice transitioned to Issued.';
    COMMENT ON COLUMN invoicing.invoices.billing_address_street1_enc IS 'PII (ADR-0011): street line 1. v1 plaintext; v2 encrypts.';
    COMMENT ON COLUMN invoicing.invoices.billing_address_street2_enc IS 'PII (ADR-0011): street line 2 (optional).';
    COMMENT ON COLUMN invoicing.invoices.billing_address_city_enc IS 'PII (ADR-0011): city.';
    COMMENT ON COLUMN invoicing.invoices.billing_address_state_enc IS 'PII (ADR-0011): state/region (optional).';
    COMMENT ON COLUMN invoicing.invoices.billing_address_postal_code_enc IS 'PII (ADR-0011): postal code.';
    COMMENT ON COLUMN invoicing.invoices.billing_address_country_code_enc IS 'ISO 3166-1 alpha-2 country code.';
    COMMENT ON COLUMN invoicing.invoices.pdf_blob_uri IS 'Presigned SAS URL to the rendered PDF in blob storage.';
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
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260508181028_AddInvoicesCreditNotesAndOutbox') THEN
    CREATE TABLE invoicing."OutboxMessages" (
        id bigint GENERATED BY DEFAULT AS IDENTITY,
        topic_name character varying(249) NOT NULL,
        kafka_key character varying(128),
        avro_payload bytea NOT NULL,
        type character varying(255) NOT NULL,
        headers character varying(8192),
        created_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_outbox_messages PRIMARY KEY (id)
    );
    COMMENT ON TABLE invoicing."OutboxMessages" IS 'Outbox pattern table for storing domain events as Avro-serialized messages for reliable event publishing.';
    COMMENT ON COLUMN invoicing."OutboxMessages".id IS 'PK, Identity';
    COMMENT ON COLUMN invoicing."OutboxMessages".topic_name IS 'The Kafka topic where this message will be published. Set by the message producer.';
    COMMENT ON COLUMN invoicing."OutboxMessages".kafka_key IS 'Kafka Key - typically the Aggregate ID for proper event ordering and partitioning';
    COMMENT ON COLUMN invoicing."OutboxMessages".avro_payload IS 'Avro-serialized domain event payload';
    COMMENT ON COLUMN invoicing."OutboxMessages".type IS 'Avro type name of the serialized event (e.g., ''FeedbackChangedEvent'') for deserialization and observability';
    COMMENT ON COLUMN invoicing."OutboxMessages".headers IS 'JSON dictionary of OpenTelemetry-standard headers for distributed tracing and metadata. Headers are automatically generated by OpenTelemetry propagators for end-to-end trace context propagation.';
    COMMENT ON COLUMN invoicing."OutboxMessages".created_utc IS 'Creation timestamp (UTC).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260508181028_AddInvoicesCreditNotesAndOutbox') THEN
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
        CONSTRAINT "PK_credit_note_lines" PRIMARY KEY (credit_note_id, line_number),
        CONSTRAINT fk_credit_note_lines_credit_notes_credit_note_id FOREIGN KEY (credit_note_id) REFERENCES invoicing.credit_notes (id) ON DELETE CASCADE
    );
    COMMENT ON TABLE invoicing.credit_note_lines IS 'CreditNote line items — sign-flipped copy of the original Invoice''s lines.';
    COMMENT ON COLUMN invoicing.credit_note_lines.line_number IS 'Position on the document (1-based).';
    COMMENT ON COLUMN invoicing.credit_note_lines.sku IS 'Catalog SKU snapshot at issuance.';
    COMMENT ON COLUMN invoicing.credit_note_lines.description IS 'Human-readable line description.';
    COMMENT ON COLUMN invoicing.credit_note_lines.quantity IS 'Units on the line (>= 1).';
    COMMENT ON COLUMN invoicing.credit_note_lines.vat_rate_percentage IS 'Applicable VAT rate, in [0, 100].';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260508181028_AddInvoicesCreditNotesAndOutbox') THEN
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
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260508181028_AddInvoicesCreditNotesAndOutbox') THEN
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
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260508181028_AddInvoicesCreditNotesAndOutbox') THEN
    CREATE INDEX "IX_CreditNotes_BuyerId" ON invoicing.credit_notes (buyer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260508181028_AddInvoicesCreditNotesAndOutbox') THEN
    CREATE UNIQUE INDEX "UX_CreditNotes_CorrelationId" ON invoicing.credit_notes (correlation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260508181028_AddInvoicesCreditNotesAndOutbox') THEN
    CREATE UNIQUE INDEX "UX_CreditNotes_CreditNoteNumber" ON invoicing.credit_notes (credit_note_number) WHERE credit_note_number IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260508181028_AddInvoicesCreditNotesAndOutbox') THEN
    CREATE UNIQUE INDEX "UX_CreditNotes_OriginalInvoiceId" ON invoicing.credit_notes (original_invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260508181028_AddInvoicesCreditNotesAndOutbox') THEN
    CREATE INDEX "IX_Invoices_BuyerId" ON invoicing.invoices (buyer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260508181028_AddInvoicesCreditNotesAndOutbox') THEN
    CREATE UNIQUE INDEX "UX_Invoices_CorrelationId" ON invoicing.invoices (correlation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260508181028_AddInvoicesCreditNotesAndOutbox') THEN
    CREATE UNIQUE INDEX "UX_Invoices_InvoiceNumber" ON invoicing.invoices (invoice_number) WHERE invoice_number IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260508181028_AddInvoicesCreditNotesAndOutbox') THEN
    CREATE UNIQUE INDEX "UX_Invoices_OrderId" ON invoicing.invoices (order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260508181028_AddInvoicesCreditNotesAndOutbox') THEN
    INSERT INTO invoicing."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260508181028_AddInvoicesCreditNotesAndOutbox', '10.0.8');
    END IF;
END $EF$;
