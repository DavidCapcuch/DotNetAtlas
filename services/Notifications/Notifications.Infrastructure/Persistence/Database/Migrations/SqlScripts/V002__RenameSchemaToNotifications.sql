
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM notifications."__EFMigrationsHistory" WHERE "migration_id" = '20260525094927_RenameSchemaToNotifications') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'notifications') THEN
            CREATE SCHEMA notifications;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM notifications."__EFMigrationsHistory" WHERE "migration_id" = '20260525094927_RenameSchemaToNotifications') THEN
    ALTER TABLE payment."OutboxMessages" SET SCHEMA notifications;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM notifications."__EFMigrationsHistory" WHERE "migration_id" = '20260525094927_RenameSchemaToNotifications') THEN
    ALTER TABLE payment."InboxMessages" SET SCHEMA notifications;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM notifications."__EFMigrationsHistory" WHERE "migration_id" = '20260525094927_RenameSchemaToNotifications') THEN
    INSERT INTO notifications."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260525094927_RenameSchemaToNotifications', '10.0.8');
    END IF;
END $EF$;
