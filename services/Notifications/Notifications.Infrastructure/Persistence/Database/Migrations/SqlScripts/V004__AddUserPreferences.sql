
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM notifications."__EFMigrationsHistory" WHERE "migration_id" = '20260609163502_AddUserPreferences') THEN
    CREATE TABLE notifications.user_preferences (
        user_id uuid NOT NULL,
        email text NOT NULL,
        phone_number text NOT NULL,
        enabled_channels text[] NOT NULL,
        quiet_hours_start time,
        quiet_hours_end time,
        time_zone text NOT NULL,
        CONSTRAINT pk_user_preferences PRIMARY KEY (user_id)
    );
    COMMENT ON TABLE notifications.user_preferences IS 'Seeded recipient preference + contact reference, keyed user_id (Keycloak sub). notifications.md §8.';
    COMMENT ON COLUMN notifications.user_preferences.user_id IS 'Recipient identity — the Keycloak sub; equals the command''s RecipientUserId.';
    COMMENT ON COLUMN notifications.user_preferences.email IS 'Email address the email dispatcher delivers to.';
    COMMENT ON COLUMN notifications.user_preferences.phone_number IS 'Fake E.164 phone number (SMS is a fake channel); consumed by the SMS dispatcher (#315).';
    COMMENT ON COLUMN notifications.user_preferences.enabled_channels IS 'Channels the recipient enabled — the left operand of enabled ∩ template_channels (§5.3).';
    COMMENT ON COLUMN notifications.user_preferences.quiet_hours_start IS 'Start of the daily quiet-hours window (civil wall-clock in time_zone); null = no quiet hours.';
    COMMENT ON COLUMN notifications.user_preferences.quiet_hours_end IS 'End of the quiet-hours window; null with quiet_hours_start (both-or-neither).';
    COMMENT ON COLUMN notifications.user_preferences.time_zone IS 'IANA time zone (e.g. Europe/Prague) the quiet-hours window is interpreted in.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM notifications."__EFMigrationsHistory" WHERE "migration_id" = '20260609163502_AddUserPreferences') THEN
    INSERT INTO notifications."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260609163502_AddUserPreferences', '10.0.8');
    END IF;
END $EF$;
