
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260526235924_SplitCreditNoteLineFromInvoiceLine') THEN
    ALTER TABLE invoicing.credit_note_lines DROP CONSTRAINT "PK_credit_note_lines";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260526235924_SplitCreditNoteLineFromInvoiceLine') THEN
    COMMENT ON TABLE invoicing.credit_note_lines IS 'CreditNoteLine items — backward-looking corrections of the source invoice''s lines.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260526235924_SplitCreditNoteLineFromInvoiceLine') THEN
    COMMENT ON COLUMN invoicing.credit_note_lines.vat_rate_percentage IS 'VAT rate from the reversed invoice line, in [0, 100].';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260526235924_SplitCreditNoteLineFromInvoiceLine') THEN
    COMMENT ON COLUMN invoicing.credit_note_lines.sku IS 'Catalog SKU snapshot from the reversed invoice line.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260526235924_SplitCreditNoteLineFromInvoiceLine') THEN
    COMMENT ON COLUMN invoicing.credit_note_lines.quantity IS 'Units being credited (>= 1).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260526235924_SplitCreditNoteLineFromInvoiceLine') THEN
    COMMENT ON COLUMN invoicing.credit_note_lines.description IS 'Human-readable line description (copied from the source invoice line).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260526235924_SplitCreditNoteLineFromInvoiceLine') THEN
    COMMENT ON COLUMN invoicing.credit_note_lines.line_number IS 'Position on the credit note (1-based; mirrors the original invoice line''s number).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260526235924_SplitCreditNoteLineFromInvoiceLine') THEN
    ALTER TABLE invoicing.credit_note_lines ADD CONSTRAINT pk_credit_note_lines PRIMARY KEY (credit_note_id, line_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260526235924_SplitCreditNoteLineFromInvoiceLine') THEN
    INSERT INTO invoicing."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260526235924_SplitCreditNoteLineFromInvoiceLine', '10.0.8');
    END IF;
END $EF$;
