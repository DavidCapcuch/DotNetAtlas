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
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260425111020_AddInvoiceNumberAllocators') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'invoicing') THEN
            CREATE SCHEMA invoicing;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260425111020_AddInvoiceNumberAllocators') THEN
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
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260425111020_AddInvoiceNumberAllocators') THEN
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
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260425111020_AddInvoiceNumberAllocators') THEN
    INSERT INTO invoicing."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260425111020_AddInvoiceNumberAllocators', '10.0.8');
    END IF;
END $EF$;
