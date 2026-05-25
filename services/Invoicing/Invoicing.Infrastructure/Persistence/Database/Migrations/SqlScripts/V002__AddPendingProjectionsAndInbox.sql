
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260426083837_AddPendingProjectionsAndInbox') THEN
    CREATE TABLE invoicing."InboxMessages" (
        message_id uuid NOT NULL,
        processed_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_inbox_messages PRIMARY KEY (message_id)
    );
    COMMENT ON TABLE invoicing."InboxMessages" IS 'Inbox pattern table for idempotent message processing. Tracks processed messages to prevent duplicate processing.';
    COMMENT ON COLUMN invoicing."InboxMessages".message_id IS 'Unique message identifier (Primary Key).';
    COMMENT ON COLUMN invoicing."InboxMessages".processed_at_utc IS 'UTC timestamp when the message was processed.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260426083837_AddPendingProjectionsAndInbox') THEN
    CREATE TABLE invoicing.pending_credit_notes (
        correlation_id uuid NOT NULL,
        order_id uuid,
        payment_id uuid,
        buyer_id uuid,
        order_payload jsonb,
        payment_payload jsonb,
        first_seen_at_utc timestamp with time zone NOT NULL,
        completed_at_utc timestamp with time zone,
        issued_credit_note_id uuid,
        CONSTRAINT pk_pending_credit_notes PRIMARY KEY (correlation_id)
    );
    COMMENT ON TABLE invoicing.pending_credit_notes IS 'Async-enrichment buffer: collects OrderCancelledEvent + PaymentRefundedEvent halves keyed on CorrelationId until M7''s IssueCreditNoteCommandHandler converts the converged row into a CreditNote aggregate.';
    COMMENT ON COLUMN invoicing.pending_credit_notes.correlation_id IS 'Saga / cross-BC correlation id. Primary key.';
    COMMENT ON COLUMN invoicing.pending_credit_notes.order_id IS 'OrderCancelledEvent.OrderId; null until the order-cancel half arrives.';
    COMMENT ON COLUMN invoicing.pending_credit_notes.payment_id IS 'PaymentRefundedEvent.PaymentTransactionId — the original captured payment, not the refund txn id.';
    COMMENT ON COLUMN invoicing.pending_credit_notes.buyer_id IS 'OrderCancelledEvent.BuyerId; M7''s outbox publisher uses this as the partition key.';
    COMMENT ON COLUMN invoicing.pending_credit_notes.order_payload IS 'PII: full OrderCancelledEvent serialised to JSON for M7 hydration.';
    COMMENT ON COLUMN invoicing.pending_credit_notes.payment_payload IS 'Full PaymentRefundedEvent serialised to JSON for M7 hydration.';
    COMMENT ON COLUMN invoicing.pending_credit_notes.first_seen_at_utc IS 'Wall-clock at first observation; never overwritten.';
    COMMENT ON COLUMN invoicing.pending_credit_notes.completed_at_utc IS 'Set when both halves are present.';
    COMMENT ON COLUMN invoicing.pending_credit_notes.issued_credit_note_id IS 'Set by M7''s IssueCreditNoteCommandHandler atomically with the CreditNote aggregate insert.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260426083837_AddPendingProjectionsAndInbox') THEN
    CREATE TABLE invoicing.pending_invoices (
        correlation_id uuid NOT NULL,
        order_id uuid,
        payment_id uuid,
        buyer_id uuid,
        order_payload jsonb,
        payment_payload jsonb,
        first_seen_at_utc timestamp with time zone NOT NULL,
        completed_at_utc timestamp with time zone,
        issued_invoice_id uuid,
        CONSTRAINT pk_pending_invoices PRIMARY KEY (correlation_id)
    );
    COMMENT ON TABLE invoicing.pending_invoices IS 'Async-enrichment buffer: collects OrderConfirmedEvent + PaymentCapturedEvent halves keyed on CorrelationId until M7''s IssueInvoiceCommandHandler converts the converged row into an Invoice aggregate.';
    COMMENT ON COLUMN invoicing.pending_invoices.correlation_id IS 'Saga / cross-BC correlation id. Primary key.';
    COMMENT ON COLUMN invoicing.pending_invoices.order_id IS 'OrderConfirmedEvent.OrderId; null until the order half arrives.';
    COMMENT ON COLUMN invoicing.pending_invoices.payment_id IS 'PaymentCapturedEvent.PaymentTransactionId; null until the payment half arrives.';
    COMMENT ON COLUMN invoicing.pending_invoices.buyer_id IS 'OrderConfirmedEvent.BuyerId; M7''s outbox publisher uses this as the partition key on invoicing.invoices.';
    COMMENT ON COLUMN invoicing.pending_invoices.order_payload IS 'PII: full OrderConfirmedEvent serialised to JSON for M7 hydration.';
    COMMENT ON COLUMN invoicing.pending_invoices.payment_payload IS 'PII: full PaymentCapturedEvent serialised to JSON for M7 hydration.';
    COMMENT ON COLUMN invoicing.pending_invoices.first_seen_at_utc IS 'Wall-clock at first observation; never overwritten on subsequent updates.';
    COMMENT ON COLUMN invoicing.pending_invoices.completed_at_utc IS 'Set when both halves are present.';
    COMMENT ON COLUMN invoicing.pending_invoices.issued_invoice_id IS 'Set by M7''s IssueInvoiceCommandHandler atomically with the Invoice aggregate insert.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260426083837_AddPendingProjectionsAndInbox') THEN
    CREATE INDEX "IX_InboxMessages_ProcessedAtUtc" ON invoicing."InboxMessages" (processed_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260426083837_AddPendingProjectionsAndInbox') THEN
    CREATE INDEX ix_pending_credit_notes_order_id ON invoicing.pending_credit_notes (order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260426083837_AddPendingProjectionsAndInbox') THEN
    CREATE INDEX ix_pending_credit_notes_ready ON invoicing.pending_credit_notes (completed_at_utc, issued_credit_note_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260426083837_AddPendingProjectionsAndInbox') THEN
    CREATE INDEX ix_pending_invoices_order_id ON invoicing.pending_invoices (order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260426083837_AddPendingProjectionsAndInbox') THEN
    CREATE INDEX ix_pending_invoices_ready ON invoicing.pending_invoices (completed_at_utc, issued_invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260426083837_AddPendingProjectionsAndInbox') THEN
    INSERT INTO invoicing."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260426083837_AddPendingProjectionsAndInbox', '10.0.8');
    END IF;
END $EF$;
