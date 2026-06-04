
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604092351_DropDedicatedCorrelationId') THEN
    ALTER TABLE invoicing.pending_invoices DROP CONSTRAINT pk_pending_invoices;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604092351_DropDedicatedCorrelationId') THEN
    DROP INDEX invoicing.ix_pending_invoices_order_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604092351_DropDedicatedCorrelationId') THEN
    ALTER TABLE invoicing.pending_credit_notes DROP CONSTRAINT pk_pending_credit_notes;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604092351_DropDedicatedCorrelationId') THEN
    DROP INDEX invoicing.ix_pending_credit_notes_order_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604092351_DropDedicatedCorrelationId') THEN
    DROP INDEX invoicing.ux_invoices_correlation_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604092351_DropDedicatedCorrelationId') THEN
    DROP INDEX invoicing.ux_credit_notes_correlation_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604092351_DropDedicatedCorrelationId') THEN
    ALTER TABLE invoicing.pending_invoices DROP COLUMN correlation_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604092351_DropDedicatedCorrelationId') THEN
    ALTER TABLE invoicing.pending_credit_notes DROP COLUMN correlation_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604092351_DropDedicatedCorrelationId') THEN
    ALTER TABLE invoicing.invoices DROP COLUMN correlation_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604092351_DropDedicatedCorrelationId') THEN
    ALTER TABLE invoicing.credit_notes DROP COLUMN correlation_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604092351_DropDedicatedCorrelationId') THEN
    COMMENT ON TABLE invoicing.pending_invoices IS 'Async-enrichment buffer: collects OrderConfirmedEvent + PaymentCapturedEvent halves keyed on OrderId until IssueInvoiceCommandHandler converts the converged row into an Invoice aggregate.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604092351_DropDedicatedCorrelationId') THEN
    COMMENT ON TABLE invoicing.pending_credit_notes IS 'Async-enrichment buffer: collects OrderCancelledEvent + PaymentRefundedEvent halves keyed on OrderId until IssueCreditNoteCommandHandler converts the converged row into a CreditNote aggregate.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604092351_DropDedicatedCorrelationId') THEN
    UPDATE invoicing.pending_invoices SET order_id = '00000000-0000-0000-0000-000000000000' WHERE order_id IS NULL;
    ALTER TABLE invoicing.pending_invoices ALTER COLUMN order_id SET NOT NULL;
    ALTER TABLE invoicing.pending_invoices ALTER COLUMN order_id SET DEFAULT '00000000-0000-0000-0000-000000000000';
    COMMENT ON COLUMN invoicing.pending_invoices.order_id IS 'OrderConfirmedEvent.OrderId; the cross-BC convergence key. Primary key.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604092351_DropDedicatedCorrelationId') THEN
    UPDATE invoicing.pending_credit_notes SET order_id = '00000000-0000-0000-0000-000000000000' WHERE order_id IS NULL;
    ALTER TABLE invoicing.pending_credit_notes ALTER COLUMN order_id SET NOT NULL;
    ALTER TABLE invoicing.pending_credit_notes ALTER COLUMN order_id SET DEFAULT '00000000-0000-0000-0000-000000000000';
    COMMENT ON COLUMN invoicing.pending_credit_notes.order_id IS 'OrderCancelledEvent.OrderId; the cross-BC convergence key. Primary key.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604092351_DropDedicatedCorrelationId') THEN
    ALTER TABLE invoicing.pending_invoices ADD CONSTRAINT pk_pending_invoices PRIMARY KEY (order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604092351_DropDedicatedCorrelationId') THEN
    ALTER TABLE invoicing.pending_credit_notes ADD CONSTRAINT pk_pending_credit_notes PRIMARY KEY (order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604092351_DropDedicatedCorrelationId') THEN
    INSERT INTO invoicing."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260604092351_DropDedicatedCorrelationId', '10.0.8');
    END IF;
END $EF$;
