
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260528172311_TrimColumnCommentNoise') THEN
    COMMENT ON TABLE invoicing.pending_invoices IS 'Async-enrichment buffer: collects OrderConfirmedEvent + PaymentCapturedEvent halves keyed on CorrelationId until IssueInvoiceCommandHandler converts the converged row into an Invoice aggregate.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260528172311_TrimColumnCommentNoise') THEN
    COMMENT ON TABLE invoicing.pending_credit_notes IS 'Async-enrichment buffer: collects OrderCancelledEvent + PaymentRefundedEvent halves keyed on CorrelationId until IssueCreditNoteCommandHandler converts the converged row into a CreditNote aggregate.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260528172311_TrimColumnCommentNoise') THEN
    COMMENT ON COLUMN invoicing.pending_invoices.payment_payload IS 'PII: full PaymentCapturedEvent serialised to JSON for issuance-time hydration.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260528172311_TrimColumnCommentNoise') THEN
    COMMENT ON COLUMN invoicing.pending_invoices.order_payload IS 'PII: full OrderConfirmedEvent serialised to JSON for issuance-time hydration.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260528172311_TrimColumnCommentNoise') THEN
    COMMENT ON COLUMN invoicing.pending_invoices.issued_invoice_id IS 'Set by IssueInvoiceCommandHandler atomically with the Invoice aggregate insert.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260528172311_TrimColumnCommentNoise') THEN
    COMMENT ON COLUMN invoicing.pending_invoices.buyer_id IS 'OrderConfirmedEvent.BuyerId; the outbox publisher uses this as the partition key on invoicing.invoices.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260528172311_TrimColumnCommentNoise') THEN
    COMMENT ON COLUMN invoicing.pending_credit_notes.payment_payload IS 'Full PaymentRefundedEvent serialised to JSON for issuance-time hydration.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260528172311_TrimColumnCommentNoise') THEN
    COMMENT ON COLUMN invoicing.pending_credit_notes.order_payload IS 'PII: full OrderCancelledEvent serialised to JSON for issuance-time hydration.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260528172311_TrimColumnCommentNoise') THEN
    COMMENT ON COLUMN invoicing.pending_credit_notes.issued_credit_note_id IS 'Set by IssueCreditNoteCommandHandler atomically with the CreditNote aggregate insert.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260528172311_TrimColumnCommentNoise') THEN
    COMMENT ON COLUMN invoicing.pending_credit_notes.buyer_id IS 'OrderCancelledEvent.BuyerId; the outbox publisher uses this as the partition key.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260528172311_TrimColumnCommentNoise') THEN
    COMMENT ON COLUMN invoicing.credit_notes.correlation_id IS 'Cancellation flow correlation id; used as idempotency key.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260528172311_TrimColumnCommentNoise') THEN
    INSERT INTO invoicing."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260528172311_TrimColumnCommentNoise', '10.0.8');
    END IF;
END $EF$;
