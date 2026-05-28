
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog."__EFMigrationsHistory" WHERE "migration_id" = '20260528172152_TrimColumnCommentNoise') THEN
    COMMENT ON COLUMN catalog.product_search_view.is_sellable IS 'Computed flag — wired up by the StockLevelChanged Kafka inbox consumer.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog."__EFMigrationsHistory" WHERE "migration_id" = '20260528172152_TrimColumnCommentNoise') THEN
    COMMENT ON COLUMN catalog.product_search_view.correlation_id IS 'Originating HTTP correlation id (ADR-0008). Populated from HttpContext.Items[CorrelationIdContextKeys.HttpContextItemsKey] by the API layer, or Guid.Empty when no HTTP pipeline is in play.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog."__EFMigrationsHistory" WHERE "migration_id" = '20260528172152_TrimColumnCommentNoise') THEN
    INSERT INTO catalog."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260528172152_TrimColumnCommentNoise', '10.0.8');
    END IF;
END $EF$;
