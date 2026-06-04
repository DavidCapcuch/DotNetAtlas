
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM notifications."__EFMigrationsHistory" WHERE "migration_id" = '20260604210531_AddNotificationDeliveries') THEN
    CREATE TABLE notifications.notification_deliveries (
        notification_id uuid NOT NULL,
        channel character varying(16) NOT NULL,
        status character varying(16) NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_notification_deliveries PRIMARY KEY (notification_id, channel)
    );
    COMMENT ON TABLE notifications.notification_deliveries IS 'Per-channel delivery ledger — idempotency + audit, keyed (notification_id, channel). ADR-0031/0032.';
    COMMENT ON COLUMN notifications.notification_deliveries.notification_id IS 'Producer-assigned notification intent identity (half of the ledger key).';
    COMMENT ON COLUMN notifications.notification_deliveries.channel IS 'Delivery channel (Email|Sms|Bell) — the other half of the ledger key.';
    COMMENT ON COLUMN notifications.notification_deliveries.status IS 'Latest recorded outcome (Dispatched|Failed).';
    COMMENT ON COLUMN notifications.notification_deliveries.created_at_utc IS 'UTC timestamp when the row was first inserted.';
    COMMENT ON COLUMN notifications.notification_deliveries.updated_at_utc IS 'UTC timestamp of the latest status write.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM notifications."__EFMigrationsHistory" WHERE "migration_id" = '20260604210531_AddNotificationDeliveries') THEN
    INSERT INTO notifications."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260604210531_AddNotificationDeliveries', '10.0.8');
    END IF;
END $EF$;
