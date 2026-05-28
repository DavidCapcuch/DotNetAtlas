
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory."__EFMigrationsHistory" WHERE "migration_id" = '20260528153746_UpdateCurrentStockLevelsCommentToUDName') THEN
    COMMENT ON TABLE inventory.current_stock_levels IS 'Denormalised read projection: one row per ProductId, mutated by CurrentStockLevelsProjectionDomainEventHandler on every ES event. Rebuildable from inventory.stock_events.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory."__EFMigrationsHistory" WHERE "migration_id" = '20260528153746_UpdateCurrentStockLevelsCommentToUDName') THEN
    INSERT INTO inventory."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260528153746_UpdateCurrentStockLevelsCommentToUDName', '10.0.8');
    END IF;
END $EF$;
