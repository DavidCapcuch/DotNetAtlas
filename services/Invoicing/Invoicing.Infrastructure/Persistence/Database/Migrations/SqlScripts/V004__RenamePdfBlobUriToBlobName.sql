
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260522180628_RenamePdfBlobUriToBlobName') THEN
    ALTER TABLE invoicing.invoices RENAME COLUMN pdf_blob_uri TO pdf_blob_name;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260522180628_RenamePdfBlobUriToBlobName') THEN
    ALTER TABLE invoicing.credit_notes RENAME COLUMN pdf_blob_uri TO pdf_blob_name;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260522180628_RenamePdfBlobUriToBlobName') THEN
    ALTER TABLE invoicing.invoices ALTER COLUMN pdf_blob_name TYPE character varying(1024);
    COMMENT ON COLUMN invoicing.invoices.pdf_blob_name IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260522180628_RenamePdfBlobUriToBlobName') THEN
    ALTER TABLE invoicing.credit_notes ALTER COLUMN pdf_blob_name TYPE character varying(1024);
    COMMENT ON COLUMN invoicing.credit_notes.pdf_blob_name IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260522180628_RenamePdfBlobUriToBlobName') THEN
    INSERT INTO invoicing."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260522180628_RenamePdfBlobUriToBlobName', '10.0.8');
    END IF;
END $EF$;
