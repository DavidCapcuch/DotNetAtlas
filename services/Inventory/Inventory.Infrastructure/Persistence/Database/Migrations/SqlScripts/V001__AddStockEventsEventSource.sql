DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'inventory') THEN
        CREATE SCHEMA inventory;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS inventory."__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory."__EFMigrationsHistory" WHERE "migration_id" = '20260424192419_AddStockEventsEventSource') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'inventory') THEN
            CREATE SCHEMA inventory;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory."__EFMigrationsHistory" WHERE "migration_id" = '20260424192419_AddStockEventsEventSource') THEN
    CREATE TABLE inventory.stock_events (
        stream_id uuid NOT NULL,
        version integer NOT NULL,
        event_type character varying(128) NOT NULL,
        payload jsonb NOT NULL,
        occurred_at_utc timestamp with time zone NOT NULL,
        appended_at_utc timestamp with time zone NOT NULL DEFAULT (now()),
        correlation_id uuid,
        CONSTRAINT pk_stock_events PRIMARY KEY (stream_id, version)
    );
    COMMENT ON TABLE inventory.stock_events IS 'Append-only event store for StockItem aggregates (ADR-0006). One row per internal ES event; composite PK (StreamId, Version) is the optimistic-concurrency mechanism.';
    COMMENT ON COLUMN inventory.stock_events.stream_id IS 'Stream identity = ProductId. One stream per StockItem.';
    COMMENT ON COLUMN inventory.stock_events.version IS 'Monotonic 1-based version per stream. Enforced by PK.';
    COMMENT ON COLUMN inventory.stock_events.event_type IS 'CLR-type name discriminator (e.g. "StockReservedDomainEvent") used by the deserializer.';
    COMMENT ON COLUMN inventory.stock_events.payload IS 'JSON-serialized internal event; stored as jsonb for legibility and indexability.';
    COMMENT ON COLUMN inventory.stock_events.occurred_at_utc IS 'UTC timestamp the domain event was produced; copied from event.OccurredOnUtc for temporal queries.';
    COMMENT ON COLUMN inventory.stock_events.appended_at_utc IS 'DB-side insert timestamp; distinguishes domain time from persisted time during replay/tests.';
    COMMENT ON COLUMN inventory.stock_events.correlation_id IS 'Saga correlation id (ADR-0008); null for ops-originated events.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory."__EFMigrationsHistory" WHERE "migration_id" = '20260424192419_AddStockEventsEventSource') THEN
    CREATE INDEX ix_stock_events_correlation ON inventory.stock_events (correlation_id) WHERE correlation_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory."__EFMigrationsHistory" WHERE "migration_id" = '20260424192419_AddStockEventsEventSource') THEN
    CREATE INDEX ix_stock_events_event_type ON inventory.stock_events (event_type);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory."__EFMigrationsHistory" WHERE "migration_id" = '20260424192419_AddStockEventsEventSource') THEN
    CREATE INDEX ix_stock_events_occurred_at ON inventory.stock_events (occurred_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory."__EFMigrationsHistory" WHERE "migration_id" = '20260424192419_AddStockEventsEventSource') THEN
    INSERT INTO inventory."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260424192419_AddStockEventsEventSource', '10.0.8');
    END IF;
END $EF$;
