
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM notifications."__EFMigrationsHistory" WHERE "migration_id" = '20260609000936_AddNotificationTemplates') THEN
    CREATE TABLE notifications.templates (
        template_key character varying(128) NOT NULL,
        description character varying(256) NOT NULL,
        CONSTRAINT pk_templates PRIMARY KEY (template_key)
    );
    COMMENT ON TABLE notifications.templates IS 'Seeded notification template reference data, keyed {bc}.{type} (lower-kebab). ADR-0032 §7.';
    COMMENT ON COLUMN notifications.templates.template_key IS 'Template identity {bounded-context}.{notification-type} (lower-kebab).';
    COMMENT ON COLUMN notifications.templates.description IS 'Human-readable description of what this template notifies about.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM notifications."__EFMigrationsHistory" WHERE "migration_id" = '20260609000936_AddNotificationTemplates') THEN
    CREATE TABLE notifications.template_channels (
        template_key character varying(128) NOT NULL,
        channel character varying(16) NOT NULL,
        subject character varying(256),
        body text NOT NULL,
        CONSTRAINT pk_template_channels PRIMARY KEY (template_key, channel),
        CONSTRAINT fk_template_channels_templates_template_key FOREIGN KEY (template_key) REFERENCES notifications.templates (template_key) ON DELETE CASCADE
    );
    COMMENT ON TABLE notifications.template_channels IS 'Per-channel template content + the supported-channel set, keyed (template_key, channel_type). ADR-0032 §7.';
    COMMENT ON COLUMN notifications.template_channels.template_key IS 'Owning template''s key (FK to templates.template_key).';
    COMMENT ON COLUMN notifications.template_channels.channel IS 'Delivery channel (Email|Sms|Bell) this content renders for.';
    COMMENT ON COLUMN notifications.template_channels.subject IS 'Subject-line template with {{token}} placeholders; null for channels without a subject.';
    COMMENT ON COLUMN notifications.template_channels.body IS 'Body template with {{token}} placeholders.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM notifications."__EFMigrationsHistory" WHERE "migration_id" = '20260609000936_AddNotificationTemplates') THEN
    INSERT INTO notifications."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260609000936_AddNotificationTemplates', '10.0.8');
    END IF;
END $EF$;
