
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog."__EFMigrationsHistory" WHERE "migration_id" = '20260530203240_UpdateIsSellableCommentToStockLevelChangedEvent') THEN
    COMMENT ON COLUMN catalog.product_search_view.is_sellable IS 'Computed flag — wired up by the StockLevelChangedEvent Kafka inbox consumer.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog."__EFMigrationsHistory" WHERE "migration_id" = '20260530203240_UpdateIsSellableCommentToStockLevelChangedEvent') THEN
    INSERT INTO catalog."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260530203240_UpdateIsSellableCommentToStockLevelChangedEvent', '10.0.8');
    END IF;
END $EF$;
