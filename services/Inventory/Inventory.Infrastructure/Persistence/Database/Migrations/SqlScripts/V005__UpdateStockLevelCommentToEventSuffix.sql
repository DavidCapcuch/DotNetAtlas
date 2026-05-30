
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory."__EFMigrationsHistory" WHERE "migration_id" = '20260530203200_UpdateStockLevelCommentToEventSuffix') THEN
    COMMENT ON COLUMN inventory.current_stock_levels.previous_available IS 'Available BEFORE the last applied event; enables StockLevelChangedEvent threshold detection without state replay.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory."__EFMigrationsHistory" WHERE "migration_id" = '20260530203200_UpdateStockLevelCommentToEventSuffix') THEN
    INSERT INTO inventory."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260530203200_UpdateStockLevelCommentToEventSuffix', '10.0.8');
    END IF;
END $EF$;
