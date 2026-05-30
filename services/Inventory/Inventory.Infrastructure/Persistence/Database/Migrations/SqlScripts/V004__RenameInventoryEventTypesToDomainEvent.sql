
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory."__EFMigrationsHistory" WHERE "migration_id" = '20260530125953_RenameInventoryEventTypesToDomainEvent') THEN
    UPDATE inventory.stock_events SET event_type = 'StockItemInitializedDomainEvent' WHERE event_type = 'StockItemInitializedEvent';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory."__EFMigrationsHistory" WHERE "migration_id" = '20260530125953_RenameInventoryEventTypesToDomainEvent') THEN
    UPDATE inventory.stock_events SET event_type = 'StockReceivedDomainEvent' WHERE event_type = 'StockReceivedEvent';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory."__EFMigrationsHistory" WHERE "migration_id" = '20260530125953_RenameInventoryEventTypesToDomainEvent') THEN
    UPDATE inventory.stock_events SET event_type = 'StockReservedDomainEvent' WHERE event_type = 'StockReservedEvent';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory."__EFMigrationsHistory" WHERE "migration_id" = '20260530125953_RenameInventoryEventTypesToDomainEvent') THEN
    UPDATE inventory.stock_events SET event_type = 'ReservationConfirmedDomainEvent' WHERE event_type = 'ReservationConfirmedEvent';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory."__EFMigrationsHistory" WHERE "migration_id" = '20260530125953_RenameInventoryEventTypesToDomainEvent') THEN
    UPDATE inventory.stock_events SET event_type = 'ReservationReleasedDomainEvent' WHERE event_type = 'ReservationReleasedEvent';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory."__EFMigrationsHistory" WHERE "migration_id" = '20260530125953_RenameInventoryEventTypesToDomainEvent') THEN
    UPDATE inventory.stock_events SET event_type = 'StockAdjustedDomainEvent' WHERE event_type = 'StockAdjustedEvent';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory."__EFMigrationsHistory" WHERE "migration_id" = '20260530125953_RenameInventoryEventTypesToDomainEvent') THEN
    INSERT INTO inventory."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260530125953_RenameInventoryEventTypesToDomainEvent', '10.0.8');
    END IF;
END $EF$;
