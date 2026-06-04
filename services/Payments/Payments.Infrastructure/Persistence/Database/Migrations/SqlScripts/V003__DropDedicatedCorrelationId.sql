
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM payments."__EFMigrationsHistory" WHERE "migration_id" = '20260604080909_DropDedicatedCorrelationId') THEN
    ALTER TABLE payments.payment_transactions DROP COLUMN correlation_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM payments."__EFMigrationsHistory" WHERE "migration_id" = '20260604080909_DropDedicatedCorrelationId') THEN
    INSERT INTO payments."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260604080909_DropDedicatedCorrelationId', '10.0.8');
    END IF;
END $EF$;
