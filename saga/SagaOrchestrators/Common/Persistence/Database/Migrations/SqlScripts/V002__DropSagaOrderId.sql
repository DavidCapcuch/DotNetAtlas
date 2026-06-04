
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260604173705_DropSagaOrderId') THEN
    DROP INDEX saga.ix_payment_processing_saga_state_order_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260604173705_DropSagaOrderId') THEN
    DROP INDEX saga.ix_checkout_saga_state_order_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260604173705_DropSagaOrderId') THEN
    ALTER TABLE saga.payment_processing_saga_state DROP COLUMN order_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260604173705_DropSagaOrderId') THEN
    ALTER TABLE saga.checkout_saga_state DROP COLUMN order_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260604173705_DropSagaOrderId') THEN
    COMMENT ON COLUMN saga.payment_processing_saga_state.correlation_id IS 'MassTransit saga instance id (ISaga.CorrelationId); equals the pre-assigned OrderId (ADR-0029).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260604173705_DropSagaOrderId') THEN
    INSERT INTO saga."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260604173705_DropSagaOrderId', '10.0.8');
    END IF;
END $EF$;
