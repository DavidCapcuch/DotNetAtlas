
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory."__EFMigrationsHistory" WHERE "migration_id" = '20260604085223_DropDedicatedCorrelationId') THEN
    DROP INDEX inventory.ix_stock_events_correlation;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory."__EFMigrationsHistory" WHERE "migration_id" = '20260604085223_DropDedicatedCorrelationId') THEN
    ALTER TABLE inventory.stock_events DROP COLUMN correlation_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory."__EFMigrationsHistory" WHERE "migration_id" = '20260604085223_DropDedicatedCorrelationId') THEN
    INSERT INTO inventory."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260604085223_DropDedicatedCorrelationId', '10.0.8');
    END IF;
END $EF$;
