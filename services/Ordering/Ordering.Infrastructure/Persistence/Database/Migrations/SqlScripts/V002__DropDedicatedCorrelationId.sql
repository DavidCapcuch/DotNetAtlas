
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ordering."__EFMigrationsHistory" WHERE "migration_id" = '20260604074459_DropDedicatedCorrelationId') THEN
    DROP INDEX ordering.ux_orders_correlation_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ordering."__EFMigrationsHistory" WHERE "migration_id" = '20260604074459_DropDedicatedCorrelationId') THEN
    ALTER TABLE ordering.orders DROP COLUMN correlation_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ordering."__EFMigrationsHistory" WHERE "migration_id" = '20260604074459_DropDedicatedCorrelationId') THEN
    INSERT INTO ordering."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260604074459_DropDedicatedCorrelationId', '10.0.8');
    END IF;
END $EF$;
