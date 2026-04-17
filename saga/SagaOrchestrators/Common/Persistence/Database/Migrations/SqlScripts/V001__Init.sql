DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'saga') THEN
        CREATE SCHEMA saga;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS saga."__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260417105325_Init') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'saga') THEN
            CREATE SCHEMA saga;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260417105325_Init') THEN
    CREATE TABLE saga."AlertSubscriptionExtensionSagaState" (
        correlation_id uuid NOT NULL,
        current_state character varying(64) NOT NULL,
        user_id uuid NOT NULL,
        payment_method_id uuid NOT NULL,
        duration_days integer NOT NULL,
        amount numeric(19,4) NOT NULL,
        currency character varying(3) NOT NULL,
        payment_transaction_id uuid,
        extension_initiated_at_utc timestamp with time zone NOT NULL,
        payment_completed_at_utc timestamp with time zone,
        created_utc timestamp with time zone NOT NULL,
        last_modified_utc timestamp with time zone NOT NULL,
        extension_completed_at_utc timestamp with time zone,
        new_expires_at_utc timestamp with time zone,
        error_message character varying(2048),
        error_code character varying(64),
        compensation_triggered boolean NOT NULL,
        compensation_completed_at_utc timestamp with time zone,
        payment_timeout_token_id uuid,
        extension_timeout_token_id uuid,
        compensation_timeout_token_id uuid,
        CONSTRAINT pk_alert_subscription_extension_saga_state PRIMARY KEY (correlation_id)
    );
    COMMENT ON TABLE saga."AlertSubscriptionExtensionSagaState" IS 'Saga state for alert subscription extension orchestration.';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".correlation_id IS 'PK - Unique correlation ID (also PaymentTransactionId)';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".current_state IS 'Current state of the saga state machine';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".user_id IS 'User who is extending the subscription';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".payment_method_id IS 'ID of the saved payment method';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".duration_days IS 'Subscription extension duration in days';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".amount IS 'Payment amount';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".currency IS 'ISO 4217 currency code';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".payment_transaction_id IS 'Payment transaction ID (set after payment completes)';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".extension_initiated_at_utc IS 'UTC timestamp when extension was initiated';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".payment_completed_at_utc IS 'UTC timestamp when payment completed (null if not completed)';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".created_utc IS 'UTC timestamp when saga was created';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".last_modified_utc IS 'UTC timestamp when saga was last updated';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".extension_completed_at_utc IS 'UTC timestamp when extension completed (null if not completed)';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".new_expires_at_utc IS 'New subscription expiration date after extension (null if not completed)';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".error_message IS 'Error message if failed';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".error_code IS 'Error code for categorized failure handling';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".compensation_triggered IS 'Whether compensation (refund) has been triggered';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".compensation_completed_at_utc IS 'UTC timestamp when compensation completed';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".payment_timeout_token_id IS 'Token ID for payment timeout scheduler - set when schedule is active';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".extension_timeout_token_id IS 'Token ID for extension timeout scheduler - set when schedule is active';
    COMMENT ON COLUMN saga."AlertSubscriptionExtensionSagaState".compensation_timeout_token_id IS 'Token ID for compensation timeout scheduler - set when schedule is active';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260417105325_Init') THEN
    CREATE TABLE saga."AlertSubscriptionPurchaseSagaState" (
        correlation_id uuid NOT NULL,
        current_state character varying(64) NOT NULL,
        user_id uuid NOT NULL,
        payment_method_id uuid NOT NULL,
        subscription_tier integer NOT NULL,
        duration_days integer NOT NULL,
        amount numeric(19,4) NOT NULL,
        currency character varying(3) NOT NULL,
        payment_transaction_id uuid,
        purchase_initiated_utc timestamp with time zone NOT NULL,
        payment_completed_utc timestamp with time zone,
        created_utc timestamp with time zone NOT NULL,
        last_modified_utc timestamp with time zone NOT NULL,
        activation_completed_utc timestamp with time zone,
        error_message character varying(2048),
        error_code character varying(64),
        compensation_triggered boolean NOT NULL,
        compensation_completed_utc timestamp with time zone,
        payment_timeout_token_id uuid,
        activation_timeout_token_id uuid,
        compensation_timeout_token_id uuid,
        CONSTRAINT pk_alert_subscription_purchase_saga_state PRIMARY KEY (correlation_id)
    );
    COMMENT ON TABLE saga."AlertSubscriptionPurchaseSagaState" IS 'Saga state for alert subscription purchase orchestration.';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".correlation_id IS 'PK - Unique correlation ID (also PaymentTransactionId)';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".current_state IS 'Current state of the saga state machine';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".user_id IS 'User who purchased the subscription';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".payment_method_id IS 'ID of the saved payment method';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".subscription_tier IS 'Subscription tier (Pro, Ultra)';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".duration_days IS 'Subscription duration in days';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".amount IS 'Payment amount';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".currency IS 'ISO 4217 currency code';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".payment_transaction_id IS 'Payment transaction ID (set after payment completes)';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".purchase_initiated_utc IS 'UTC timestamp when purchase was initiated';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".payment_completed_utc IS 'UTC timestamp when payment completed (null if not completed)';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".created_utc IS 'UTC timestamp when saga was created';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".last_modified_utc IS 'UTC timestamp when saga was last updated';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".activation_completed_utc IS 'UTC timestamp when activation completed (null if not completed)';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".error_message IS 'Error message if failed';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".error_code IS 'Error code for categorized failure handling';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".compensation_triggered IS 'Whether compensation (refund) has been triggered';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".compensation_completed_utc IS 'UTC timestamp when compensation completed';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".payment_timeout_token_id IS 'Token ID for payment timeout scheduler - set when schedule is active';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".activation_timeout_token_id IS 'Token ID for activation timeout scheduler - set when schedule is active';
    COMMENT ON COLUMN saga."AlertSubscriptionPurchaseSagaState".compensation_timeout_token_id IS 'Token ID for compensation timeout scheduler - set when schedule is active';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260417105325_Init') THEN
    CREATE TABLE saga."OutboxMessages" (
        id bigint GENERATED BY DEFAULT AS IDENTITY,
        topic_name character varying(249) NOT NULL,
        kafka_key character varying(128),
        avro_payload bytea NOT NULL,
        type character varying(255) NOT NULL,
        headers character varying(8192),
        created_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_outbox_messages PRIMARY KEY (id)
    );
    COMMENT ON TABLE saga."OutboxMessages" IS 'Outbox pattern table for storing domain events as Avro-serialized messages for reliable event publishing.';
    COMMENT ON COLUMN saga."OutboxMessages".id IS 'PK, Identity';
    COMMENT ON COLUMN saga."OutboxMessages".topic_name IS 'The Kafka topic where this message will be published. Set by the message producer.';
    COMMENT ON COLUMN saga."OutboxMessages".kafka_key IS 'Kafka Key - typically the Aggregate ID for proper event ordering and partitioning';
    COMMENT ON COLUMN saga."OutboxMessages".avro_payload IS 'Avro-serialized domain event payload';
    COMMENT ON COLUMN saga."OutboxMessages".type IS 'Avro type name of the serialized event (e.g., ''FeedbackChangedEvent'') for deserialization and observability';
    COMMENT ON COLUMN saga."OutboxMessages".headers IS 'JSON dictionary of OpenTelemetry-standard headers for distributed tracing and metadata. Headers are automatically generated by OpenTelemetry propagators for end-to-end trace context propagation.';
    COMMENT ON COLUMN saga."OutboxMessages".created_utc IS 'Creation timestamp (UTC).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260417105325_Init') THEN
    CREATE TABLE saga."PaymentProcessingSagaState" (
        correlation_id uuid NOT NULL,
        current_state character varying(64) NOT NULL,
        user_id uuid NOT NULL,
        payment_method_id uuid NOT NULL,
        amount numeric(19,4) NOT NULL,
        currency character varying(3) NOT NULL,
        idempotency_key character varying(128) NOT NULL,
        authorization_id character varying(256),
        authorization_expires_at_utc timestamp with time zone,
        payment_transaction_id uuid,
        initiated_at_utc timestamp with time zone NOT NULL,
        created_utc timestamp with time zone NOT NULL,
        last_modified_utc timestamp with time zone NOT NULL,
        authorized_at_utc timestamp with time zone,
        captured_at_utc timestamp with time zone,
        authorization_retry_count integer NOT NULL DEFAULT 0,
        capture_retry_count integer NOT NULL DEFAULT 0,
        error_code character varying(64),
        error_message character varying(2048),
        compensation_triggered boolean NOT NULL,
        compensation_completed_at_utc timestamp with time zone,
        authorization_timeout_token_id uuid,
        capture_timeout_token_id uuid,
        void_timeout_token_id uuid,
        refund_timeout_token_id uuid,
        success_finalization_timeout_token_id uuid,
        CONSTRAINT pk_payment_processing_saga_state PRIMARY KEY (correlation_id)
    );
    COMMENT ON TABLE saga."PaymentProcessingSagaState" IS 'Saga state for payment processing orchestration.';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".correlation_id IS 'Unique correlation ID for the payment saga';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".current_state IS 'Current state of the saga state machine';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".user_id IS 'User initiating the payment';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".payment_method_id IS 'ID of the saved payment method';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".amount IS 'Payment amount';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".currency IS 'ISO 4217 currency code';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".idempotency_key IS 'Idempotency key to prevent duplicate processing';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".authorization_id IS 'Authorization ID from payment provider';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".authorization_expires_at_utc IS 'UTC timestamp when authorization expires';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".payment_transaction_id IS 'Payment transaction ID after capture';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".initiated_at_utc IS 'UTC timestamp when payment was initiated';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".created_utc IS 'UTC timestamp when saga was created';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".last_modified_utc IS 'UTC timestamp when saga was last updated';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".authorized_at_utc IS 'UTC timestamp when authorization completed';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".captured_at_utc IS 'UTC timestamp when capture completed';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".authorization_retry_count IS 'Number of authorization retry attempts';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".capture_retry_count IS 'Number of capture retry attempts';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".error_code IS 'Error code for categorized failure handling';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".error_message IS 'Error message if failed';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".compensation_triggered IS 'Whether compensation has been triggered';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".compensation_completed_at_utc IS 'UTC timestamp when compensation completed';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".authorization_timeout_token_id IS 'Token ID for authorization timeout scheduler - set when schedule is active';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".capture_timeout_token_id IS 'Token ID for capture timeout scheduler - set when schedule is active';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".void_timeout_token_id IS 'Token ID for void timeout scheduler - set when schedule is active';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".refund_timeout_token_id IS 'Token ID for refund timeout scheduler - set when schedule is active';
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".success_finalization_timeout_token_id IS 'Token ID for success finalization timeout scheduler - set when schedule is active';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260417105325_Init') THEN
    CREATE INDEX "IX_SubscriptionExtensionSagaState_CurrentState" ON saga."AlertSubscriptionExtensionSagaState" (current_state);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260417105325_Init') THEN
    CREATE INDEX "IX_SubscriptionExtensionSagaState_State_Created" ON saga."AlertSubscriptionExtensionSagaState" (current_state, created_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260417105325_Init') THEN
    CREATE INDEX "IX_SubscriptionExtensionSagaState_State_LastUpdated" ON saga."AlertSubscriptionExtensionSagaState" (current_state, last_modified_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260417105325_Init') THEN
    CREATE INDEX "IX_SubscriptionExtensionSagaState_UserId" ON saga."AlertSubscriptionExtensionSagaState" (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260417105325_Init') THEN
    CREATE INDEX "IX_SubscriptionPurchaseSagaState_CurrentState" ON saga."AlertSubscriptionPurchaseSagaState" (current_state);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260417105325_Init') THEN
    CREATE INDEX "IX_SubscriptionPurchaseSagaState_State_Created" ON saga."AlertSubscriptionPurchaseSagaState" (current_state, created_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260417105325_Init') THEN
    CREATE INDEX "IX_SubscriptionPurchaseSagaState_State_LastUpdated" ON saga."AlertSubscriptionPurchaseSagaState" (current_state, last_modified_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260417105325_Init') THEN
    CREATE INDEX "IX_SubscriptionPurchaseSagaState_UserId" ON saga."AlertSubscriptionPurchaseSagaState" (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260417105325_Init') THEN
    CREATE INDEX "IX_PaymentSagaState_CurrentState" ON saga."PaymentProcessingSagaState" (current_state);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260417105325_Init') THEN
    CREATE UNIQUE INDEX "IX_PaymentSagaState_IdempotencyKey" ON saga."PaymentProcessingSagaState" (idempotency_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260417105325_Init') THEN
    CREATE INDEX "IX_PaymentSagaState_State_Created" ON saga."PaymentProcessingSagaState" (current_state, created_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260417105325_Init') THEN
    CREATE INDEX "IX_PaymentSagaState_State_LastUpdated" ON saga."PaymentProcessingSagaState" (current_state, last_modified_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260417105325_Init') THEN
    CREATE INDEX "IX_PaymentSagaState_UserId" ON saga."PaymentProcessingSagaState" (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260417105325_Init') THEN
    INSERT INTO saga."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260417105325_Init', '10.0.5');
    END IF;
END $EF$;
