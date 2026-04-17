DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'weather') THEN
        CREATE SCHEMA weather;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS weather."__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weather."__EFMigrationsHistory" WHERE "migration_id" = '20260416193112_Init') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'weather') THEN
            CREATE SCHEMA weather;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weather."__EFMigrationsHistory" WHERE "migration_id" = '20260416193112_Init') THEN
    CREATE TABLE weather.alert_subscribers (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        subscription_tier integer NOT NULL,
        subscription_expiry_at_utc timestamp with time zone,
        last_paid_subscription_ended_at_utc timestamp with time zone,
        temperature_unit_preference integer NOT NULL,
        wind_speed_unit_preference integer NOT NULL,
        created_utc timestamp with time zone NOT NULL,
        last_modified_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_alert_subscribers PRIMARY KEY (id)
    );
    COMMENT ON TABLE weather.alert_subscribers IS 'Contains subscribers for weather alert subscriptions.';
    COMMENT ON COLUMN weather.alert_subscribers.id IS 'PK';
    COMMENT ON COLUMN weather.alert_subscribers.user_id IS 'User who subscribed for weather alerts.';
    COMMENT ON COLUMN weather.alert_subscribers.subscription_tier IS 'Subscription tier (Free, Pro, Ultra).';
    COMMENT ON COLUMN weather.alert_subscribers.subscription_expiry_at_utc IS 'Expiry date for subscription (UTC). Null for free tier.';
    COMMENT ON COLUMN weather.alert_subscribers.last_paid_subscription_ended_at_utc IS 'When the last paid subscription ended. Null if never had paid subscription.';
    COMMENT ON COLUMN weather.alert_subscribers.temperature_unit_preference IS 'Preferred temperature unit (Celsius, Fahrenheit, Kelvin).';
    COMMENT ON COLUMN weather.alert_subscribers.wind_speed_unit_preference IS 'Preferred wind speed unit (KilometersPerHour, MilesPerHour).';
    COMMENT ON COLUMN weather.alert_subscribers.created_utc IS 'Timestamp when user first subscribed (UTC).';
    COMMENT ON COLUMN weather.alert_subscribers.last_modified_utc IS 'Timestamp when subscription was last modified (UTC).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weather."__EFMigrationsHistory" WHERE "migration_id" = '20260416193112_Init') THEN
    CREATE TABLE weather.feedbacks (
        id uuid NOT NULL,
        created_by_user uuid NOT NULL,
        created_utc timestamp with time zone NOT NULL,
        last_modified_utc timestamp with time zone NOT NULL,
        "Feedback" character varying(500) NOT NULL,
        "Rating" smallint NOT NULL,
        CONSTRAINT pk_feedbacks PRIMARY KEY (id)
    );
    COMMENT ON TABLE weather.feedbacks IS 'Contains user feedbacks about the weather.';
    COMMENT ON COLUMN weather.feedbacks.id IS 'PK';
    COMMENT ON COLUMN weather.feedbacks.created_by_user IS 'User who created the feedback.';
    COMMENT ON COLUMN weather.feedbacks.created_utc IS 'Creation timestamp (UTC).';
    COMMENT ON COLUMN weather.feedbacks.last_modified_utc IS 'Last modification timestamp (UTC).';
    COMMENT ON COLUMN weather.feedbacks."Feedback" IS 'Weather feedback from the user.';
    COMMENT ON COLUMN weather.feedbacks."Rating" IS 'Rating given by the user.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weather."__EFMigrationsHistory" WHERE "migration_id" = '20260416193112_Init') THEN
    CREATE TABLE weather."InboxMessages" (
        message_id uuid NOT NULL,
        processed_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_inbox_messages PRIMARY KEY (message_id)
    );
    COMMENT ON TABLE weather."InboxMessages" IS 'Inbox pattern table for idempotent message processing. Tracks processed messages to prevent duplicate processing.';
    COMMENT ON COLUMN weather."InboxMessages".message_id IS 'Unique message identifier (Primary Key).';
    COMMENT ON COLUMN weather."InboxMessages".processed_at_utc IS 'UTC timestamp when the message was processed.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weather."__EFMigrationsHistory" WHERE "migration_id" = '20260416193112_Init') THEN
    CREATE TABLE weather.locations (
        id uuid NOT NULL,
        country_code integer NOT NULL,
        created_utc timestamp with time zone NOT NULL,
        last_modified_utc timestamp with time zone NOT NULL,
        "City" character varying(100) NOT NULL,
        CONSTRAINT pk_locations PRIMARY KEY (id)
    );
    COMMENT ON TABLE weather.locations IS 'Contains city-country locations.';
    COMMENT ON COLUMN weather.locations.id IS 'PK';
    COMMENT ON COLUMN weather.locations.country_code IS 'ISO 3166-1 alpha-2 country code.';
    COMMENT ON COLUMN weather.locations.created_utc IS 'Creation timestamp (UTC).';
    COMMENT ON COLUMN weather.locations.last_modified_utc IS 'Last modification timestamp (UTC).';
    COMMENT ON COLUMN weather.locations."City" IS 'Name of the city.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weather."__EFMigrationsHistory" WHERE "migration_id" = '20260416193112_Init') THEN
    CREATE TABLE weather."OutboxMessages" (
        id bigint GENERATED BY DEFAULT AS IDENTITY,
        topic_name character varying(249) NOT NULL,
        kafka_key character varying(128),
        avro_payload bytea NOT NULL,
        type character varying(255) NOT NULL,
        headers character varying(8192),
        created_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_outbox_messages PRIMARY KEY (id)
    );
    COMMENT ON TABLE weather."OutboxMessages" IS 'Outbox pattern table for storing domain events as Avro-serialized messages for reliable event publishing.';
    COMMENT ON COLUMN weather."OutboxMessages".id IS 'PK, Identity';
    COMMENT ON COLUMN weather."OutboxMessages".topic_name IS 'The Kafka topic where this message will be published. Set by the message producer.';
    COMMENT ON COLUMN weather."OutboxMessages".kafka_key IS 'Kafka Key - typically the Aggregate ID for proper event ordering and partitioning';
    COMMENT ON COLUMN weather."OutboxMessages".avro_payload IS 'Avro-serialized domain event payload';
    COMMENT ON COLUMN weather."OutboxMessages".type IS 'Avro type name of the serialized event (e.g., ''FeedbackChangedEvent'') for deserialization and observability';
    COMMENT ON COLUMN weather."OutboxMessages".headers IS 'JSON dictionary of OpenTelemetry-standard headers for distributed tracing and metadata. Headers are automatically generated by OpenTelemetry propagators for end-to-end trace context propagation.';
    COMMENT ON COLUMN weather."OutboxMessages".created_utc IS 'Creation timestamp (UTC).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weather."__EFMigrationsHistory" WHERE "migration_id" = '20260416193112_Init') THEN
    CREATE TABLE weather.monitored_location_alerts_subscriptions (
        id uuid NOT NULL,
        monitored_location_id uuid NOT NULL,
        created_utc timestamp with time zone NOT NULL,
        last_modified_utc timestamp with time zone NOT NULL,
        alert_subscriber_id uuid,
        CONSTRAINT pk_monitored_location_alerts_subscriptions PRIMARY KEY (id),
        CONSTRAINT fk_monitored_location_alerts_subscriptions_alert_subscribers_a FOREIGN KEY (alert_subscriber_id) REFERENCES weather.alert_subscribers (id) ON DELETE RESTRICT
    );
    COMMENT ON TABLE weather.monitored_location_alerts_subscriptions IS 'Contains user subscriptions to monitored location weather alerts.';
    COMMENT ON COLUMN weather.monitored_location_alerts_subscriptions.id IS 'PK';
    COMMENT ON COLUMN weather.monitored_location_alerts_subscriptions.monitored_location_id IS 'FK to MonitoredLocation (ID reference only, no navigation).';
    COMMENT ON COLUMN weather.monitored_location_alerts_subscriptions.created_utc IS 'Creation timestamp (UTC).';
    COMMENT ON COLUMN weather.monitored_location_alerts_subscriptions.last_modified_utc IS 'Last modification timestamp (UTC).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weather."__EFMigrationsHistory" WHERE "migration_id" = '20260416193112_Init') THEN
    CREATE TABLE weather.monitored_locations (
        id uuid NOT NULL,
        location_id uuid NOT NULL,
        is_active boolean NOT NULL,
        created_utc timestamp with time zone NOT NULL,
        last_modified_utc timestamp with time zone NOT NULL,
        "HighHumidityThresholdPercent" double precision NOT NULL,
        "HighTemperatureThresholdC" double precision NOT NULL,
        "HighWindSpeedThresholdKmh" double precision NOT NULL,
        "LowHumidityThresholdPercent" double precision NOT NULL,
        "LowTemperatureThresholdC" double precision NOT NULL,
        recent_readings jsonb,
        CONSTRAINT pk_monitored_locations PRIMARY KEY (id),
        CONSTRAINT fk_monitored_locations_locations_location_id FOREIGN KEY (location_id) REFERENCES weather.locations (id) ON DELETE RESTRICT
    );
    COMMENT ON TABLE weather.monitored_locations IS 'Contains monitored locations with weather sensor data and alert thresholds.';
    COMMENT ON COLUMN weather.monitored_locations.id IS 'PK';
    COMMENT ON COLUMN weather.monitored_locations.is_active IS 'Whether this location is actively being monitored.';
    COMMENT ON COLUMN weather.monitored_locations.created_utc IS 'Creation timestamp (UTC).';
    COMMENT ON COLUMN weather.monitored_locations.last_modified_utc IS 'Last modification timestamp (UTC).';
    COMMENT ON COLUMN weather.monitored_locations."HighHumidityThresholdPercent" IS 'Humidity threshold for high humidity alerts (%).';
    COMMENT ON COLUMN weather.monitored_locations."HighTemperatureThresholdC" IS 'Temperature threshold for high temperature alerts (°C).';
    COMMENT ON COLUMN weather.monitored_locations."HighWindSpeedThresholdKmh" IS 'Wind speed threshold for high wind alerts (km/h).';
    COMMENT ON COLUMN weather.monitored_locations."LowHumidityThresholdPercent" IS 'Humidity threshold for low humidity alerts (%).';
    COMMENT ON COLUMN weather.monitored_locations."LowTemperatureThresholdC" IS 'Temperature threshold for low temperature alerts (°C).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weather."__EFMigrationsHistory" WHERE "migration_id" = '20260416193112_Init') THEN
    CREATE INDEX "IX_Subscribers_SubscriptionTier_ExpiryUtc" ON weather.alert_subscribers (subscription_tier, subscription_expiry_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weather."__EFMigrationsHistory" WHERE "migration_id" = '20260416193112_Init') THEN
    CREATE UNIQUE INDEX "UX_Subscribers_UserId" ON weather.alert_subscribers (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weather."__EFMigrationsHistory" WHERE "migration_id" = '20260416193112_Init') THEN
    CREATE UNIQUE INDEX "UX_WeatherFeedback_CreatedByUser" ON weather.feedbacks (created_by_user);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weather."__EFMigrationsHistory" WHERE "migration_id" = '20260416193112_Init') THEN
    CREATE INDEX "IX_InboxMessages_ProcessedAtUtc" ON weather."InboxMessages" (processed_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weather."__EFMigrationsHistory" WHERE "migration_id" = '20260416193112_Init') THEN
    CREATE INDEX ix_monitored_location_alerts_subscriptions_alert_subscriber_id ON weather.monitored_location_alerts_subscriptions (alert_subscriber_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weather."__EFMigrationsHistory" WHERE "migration_id" = '20260416193112_Init') THEN
    CREATE INDEX "IX_MonitoredLocationAlertsSubscriptions_MonitoredLocationId" ON weather.monitored_location_alerts_subscriptions (monitored_location_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weather."__EFMigrationsHistory" WHERE "migration_id" = '20260416193112_Init') THEN
    CREATE UNIQUE INDEX ix_monitored_locations_location_id ON weather.monitored_locations (location_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weather."__EFMigrationsHistory" WHERE "migration_id" = '20260416193112_Init') THEN
    INSERT INTO weather."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260416193112_Init', '10.0.5');
    END IF;
END $EF$;
