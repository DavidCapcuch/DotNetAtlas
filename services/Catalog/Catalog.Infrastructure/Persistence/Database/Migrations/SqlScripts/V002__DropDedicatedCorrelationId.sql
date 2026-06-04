
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog."__EFMigrationsHistory" WHERE "migration_id" = '20260604071307_DropDedicatedCorrelationId') THEN
    ALTER TABLE catalog.product_search_view DROP COLUMN correlation_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog."__EFMigrationsHistory" WHERE "migration_id" = '20260604071307_DropDedicatedCorrelationId') THEN
    INSERT INTO catalog."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260604071307_DropDedicatedCorrelationId', '10.0.8');
    END IF;
END $EF$;
