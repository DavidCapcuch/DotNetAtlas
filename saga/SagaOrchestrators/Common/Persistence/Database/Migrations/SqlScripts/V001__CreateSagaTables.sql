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
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260602215520_CreateSagaTables') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'saga') THEN
            CREATE SCHEMA saga;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260602215520_CreateSagaTables') THEN
    CREATE TABLE saga.checkout_saga_state (
        correlation_id uuid NOT NULL,
        current_state character varying(64) NOT NULL,
        user_id uuid NOT NULL,
        total_amount numeric(19,4) NOT NULL,
        currency character varying(3) NOT NULL,
        payment_method_id uuid NOT NULL,
        basket_snapshot_json jsonb NOT NULL,
        shipping_address_json jsonb,
        billing_address_json jsonb,
        initiated_at_utc timestamp with time zone NOT NULL,
        order_id uuid,
        order_created_at_utc timestamp with time zone,
        reservation_ids_json jsonb NOT NULL,
        expected_reservations integer NOT NULL DEFAULT 0,
        pending_reservations integer NOT NULL DEFAULT 0,
        stock_reservation_started_at_utc timestamp with time zone,
        stock_reservation_completed_at_utc timestamp with time zone,
        payment_transaction_id uuid,
        payment_requested_at_utc timestamp with time zone,
        payment_completed_at_utc timestamp with time zone,
        order_confirmation_requested_at_utc timestamp with time zone,
        order_confirmed_at_utc timestamp with time zone,
        pending_releases integer NOT NULL DEFAULT 0,
        order_cancelled_received boolean NOT NULL DEFAULT FALSE,
        compensation_started_at_utc timestamp with time zone,
        compensation_completed_at_utc timestamp with time zone,
        compensation_triggered boolean NOT NULL,
        error_code character varying(100),
        error_message character varying(2048),
        failed_at_state character varying(64),
        order_creation_timeout_token_id uuid,
        stock_reservation_timeout_token_id uuid,
        payment_timeout_token_id uuid,
        order_confirmation_timeout_token_id uuid,
        compensation_timeout_token_id uuid,
        created_utc timestamp with time zone NOT NULL,
        last_modified_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_checkout_saga_state PRIMARY KEY (correlation_id)
    );
    COMMENT ON TABLE saga.checkout_saga_state IS 'Saga state for the checkout orchestration.';
    COMMENT ON COLUMN saga.checkout_saga_state.correlation_id IS 'Workflow correlation id - equals BasketCheckoutInitiatedEvent.BasketCorrelationId (ADR-0008).';
    COMMENT ON COLUMN saga.checkout_saga_state.current_state IS 'Current state of the saga state machine.';
    COMMENT ON COLUMN saga.checkout_saga_state.user_id IS 'User initiating checkout. Becomes Ordering''s BuyerId.';
    COMMENT ON COLUMN saga.checkout_saga_state.total_amount IS 'Sum of basket line totals captured at checkout initiation.';
    COMMENT ON COLUMN saga.checkout_saga_state.currency IS 'ISO 4217 currency code.';
    COMMENT ON COLUMN saga.checkout_saga_state.payment_method_id IS 'Saved payment method id (Guid). Stored as uuid because Basket + Ordering wire shapes still use Guid; CheckoutSaga string-encodes it only at the Payments-emit boundary (C-2 closeout — Payments-side schema changed, upstream BC wire shapes deferred).';
    COMMENT ON COLUMN saga.checkout_saga_state.basket_snapshot_json IS 'Serialized basket line snapshot (immutable for the saga''s lifetime).';
    COMMENT ON COLUMN saga.checkout_saga_state.shipping_address_json IS 'Serialized shipping Address value object. Nulled out on terminal per ADR-0011.';
    COMMENT ON COLUMN saga.checkout_saga_state.billing_address_json IS 'Serialized billing Address value object. Nulled out on terminal per ADR-0011.';
    COMMENT ON COLUMN saga.checkout_saga_state.initiated_at_utc IS 'UTC timestamp when the saga was initiated (copied from the Basket event).';
    COMMENT ON COLUMN saga.checkout_saga_state.order_id IS 'Ordering aggregate id assigned after OrderCreatedEvent. Null until OrderCreated arrives.';
    COMMENT ON COLUMN saga.checkout_saga_state.order_created_at_utc IS 'UTC timestamp when Ordering reported the order created.';
    COMMENT ON COLUMN saga.checkout_saga_state.reservation_ids_json IS 'Serialized per-ProductId reservation tracking dictionary.';
    COMMENT ON COLUMN saga.checkout_saga_state.expected_reservations IS 'Number of distinct ProductIds in the basket - target reservation count.';
    COMMENT ON COLUMN saga.checkout_saga_state.pending_reservations IS 'Decremented on each StockReservedSagaEvent. Zero triggers AwaitingPayment.';
    COMMENT ON COLUMN saga.checkout_saga_state.stock_reservation_started_at_utc IS 'UTC timestamp when stock reservation fan-out began.';
    COMMENT ON COLUMN saga.checkout_saga_state.stock_reservation_completed_at_utc IS 'UTC timestamp when all reservations completed.';
    COMMENT ON COLUMN saga.checkout_saga_state.payment_transaction_id IS 'Payment transaction id from PaymentProcessingSaga. Required for refund compensation.';
    COMMENT ON COLUMN saga.checkout_saga_state.payment_requested_at_utc IS 'UTC timestamp when RequestPaymentCommand was emitted to payments.payment-commands (per ADR-0023; renamed from PaymentRequestedEvent).';
    COMMENT ON COLUMN saga.checkout_saga_state.payment_completed_at_utc IS 'UTC timestamp when PaymentCompletedSagaEvent was received.';
    COMMENT ON COLUMN saga.checkout_saga_state.order_confirmation_requested_at_utc IS 'UTC timestamp when ConfirmOrderCommand was dispatched.';
    COMMENT ON COLUMN saga.checkout_saga_state.order_confirmed_at_utc IS 'UTC timestamp when OrderConfirmedSagaEvent arrived.';
    COMMENT ON COLUMN saga.checkout_saga_state.pending_releases IS 'Decremented on each ReservationReleasedSagaEvent during compensation. Zero AND OrderCancelledReceived=true gates the transition to Compensated.';
    COMMENT ON COLUMN saga.checkout_saga_state.order_cancelled_received IS 'True once OrderCancelledSagaEvent has been observed during compensation - gates the transition to Compensated.';
    COMMENT ON COLUMN saga.checkout_saga_state.compensation_started_at_utc IS 'UTC timestamp at first transition into any Compensating* state.';
    COMMENT ON COLUMN saga.checkout_saga_state.compensation_completed_at_utc IS 'UTC timestamp at transition into Compensated.';
    COMMENT ON COLUMN saga.checkout_saga_state.compensation_triggered IS 'Set true on the first Compensating* transition.';
    COMMENT ON COLUMN saga.checkout_saga_state.error_code IS 'Categorised failure code (e.g., STOCK_UNAVAILABLE, PAYMENT_FAILED).';
    COMMENT ON COLUMN saga.checkout_saga_state.error_message IS 'Human-readable failure message.';
    COMMENT ON COLUMN saga.checkout_saga_state.failed_at_state IS 'Name of the state when failure first occurred. Aids ops forensics.';
    COMMENT ON COLUMN saga.checkout_saga_state.order_creation_timeout_token_id IS 'Token ID for the order-creation timeout scheduler - set when schedule is active.';
    COMMENT ON COLUMN saga.checkout_saga_state.stock_reservation_timeout_token_id IS 'Token ID for the stock-reservation timeout scheduler - set when schedule is active.';
    COMMENT ON COLUMN saga.checkout_saga_state.payment_timeout_token_id IS 'Token ID for the payment timeout scheduler - set when schedule is active.';
    COMMENT ON COLUMN saga.checkout_saga_state.order_confirmation_timeout_token_id IS 'Token ID for the order-confirmation timeout scheduler - set when schedule is active.';
    COMMENT ON COLUMN saga.checkout_saga_state.compensation_timeout_token_id IS 'Token ID for the compensation timeout scheduler - set when schedule is active.';
    COMMENT ON COLUMN saga.checkout_saga_state.created_utc IS 'UTC timestamp when saga row was created.';
    COMMENT ON COLUMN saga.checkout_saga_state.last_modified_utc IS 'UTC timestamp when saga row was last mutated.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260602215520_CreateSagaTables') THEN
    CREATE TABLE saga.outbox_messages (
        id bigint GENERATED BY DEFAULT AS IDENTITY,
        topic_name character varying(249) NOT NULL,
        kafka_key character varying(128),
        avro_payload bytea NOT NULL,
        type character varying(255) NOT NULL,
        headers character varying(8192),
        created_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_outbox_messages PRIMARY KEY (id)
    );
    COMMENT ON TABLE saga.outbox_messages IS 'Outbox pattern table for storing domain events as Avro-serialized messages for reliable event publishing.';
    COMMENT ON COLUMN saga.outbox_messages.id IS 'PK, Identity';
    COMMENT ON COLUMN saga.outbox_messages.topic_name IS 'The Kafka topic where this message will be published. Set by the message producer.';
    COMMENT ON COLUMN saga.outbox_messages.kafka_key IS 'Kafka Key - typically the Aggregate ID for proper event ordering and partitioning';
    COMMENT ON COLUMN saga.outbox_messages.avro_payload IS 'Avro-serialized domain event payload';
    COMMENT ON COLUMN saga.outbox_messages.type IS 'Avro type name of the serialized event (e.g., ''FeedbackChangedEvent'') for deserialization and observability';
    COMMENT ON COLUMN saga.outbox_messages.headers IS 'JSON dictionary of OpenTelemetry-standard headers for distributed tracing and metadata. Headers are automatically generated by OpenTelemetry propagators for end-to-end trace context propagation.';
    COMMENT ON COLUMN saga.outbox_messages.created_utc IS 'Creation timestamp (UTC).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260602215520_CreateSagaTables') THEN
    CREATE TABLE saga.payment_processing_saga_state (
        correlation_id uuid NOT NULL,
        current_state character varying(64) NOT NULL,
        order_id uuid NOT NULL,
        user_id uuid NOT NULL,
        payment_method_id character varying(64) NOT NULL,
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
        error_code character varying(100),
        error_message character varying(2048),
        compensation_triggered boolean NOT NULL,
        compensation_completed_at_utc timestamp with time zone,
        authorization_timeout_token_id uuid,
        capture_approval_timeout_token_id uuid,
        capture_timeout_token_id uuid,
        void_timeout_token_id uuid,
        CONSTRAINT pk_payment_processing_saga_state PRIMARY KEY (correlation_id)
    );
    COMMENT ON TABLE saga.payment_processing_saga_state IS 'Saga state for payment processing orchestration.';
    COMMENT ON COLUMN saga.payment_processing_saga_state.correlation_id IS 'Unique correlation ID for the payment saga';
    COMMENT ON COLUMN saga.payment_processing_saga_state.current_state IS 'Current state of the saga state machine';
    COMMENT ON COLUMN saga.payment_processing_saga_state.order_id IS 'Ordering aggregate id this payment is attached to. Frozen at saga start.';
    COMMENT ON COLUMN saga.payment_processing_saga_state.user_id IS 'User initiating the payment';
    COMMENT ON COLUMN saga.payment_processing_saga_state.payment_method_id IS 'Gateway-issued opaque payment-method token (Stripe ''pm_*'', Adyen alphanumeric, …); 1-64 chars. Changed from uuid in the Wave-1 closeout C-2 fix.';
    COMMENT ON COLUMN saga.payment_processing_saga_state.amount IS 'Payment amount';
    COMMENT ON COLUMN saga.payment_processing_saga_state.currency IS 'ISO 4217 currency code';
    COMMENT ON COLUMN saga.payment_processing_saga_state.idempotency_key IS 'Idempotency key to prevent duplicate processing';
    COMMENT ON COLUMN saga.payment_processing_saga_state.authorization_id IS 'Authorization ID from payment provider';
    COMMENT ON COLUMN saga.payment_processing_saga_state.authorization_expires_at_utc IS 'UTC timestamp when authorization expires';
    COMMENT ON COLUMN saga.payment_processing_saga_state.payment_transaction_id IS 'Payment transaction ID after capture';
    COMMENT ON COLUMN saga.payment_processing_saga_state.initiated_at_utc IS 'UTC timestamp when payment was initiated';
    COMMENT ON COLUMN saga.payment_processing_saga_state.created_utc IS 'UTC timestamp when saga was created';
    COMMENT ON COLUMN saga.payment_processing_saga_state.last_modified_utc IS 'UTC timestamp when saga was last updated';
    COMMENT ON COLUMN saga.payment_processing_saga_state.authorized_at_utc IS 'UTC timestamp when authorization completed';
    COMMENT ON COLUMN saga.payment_processing_saga_state.captured_at_utc IS 'UTC timestamp when capture completed';
    COMMENT ON COLUMN saga.payment_processing_saga_state.authorization_retry_count IS 'Number of authorization retry attempts';
    COMMENT ON COLUMN saga.payment_processing_saga_state.capture_retry_count IS 'Number of capture retry attempts';
    COMMENT ON COLUMN saga.payment_processing_saga_state.error_code IS 'Error code for categorized failure handling';
    COMMENT ON COLUMN saga.payment_processing_saga_state.error_message IS 'Error message if failed';
    COMMENT ON COLUMN saga.payment_processing_saga_state.compensation_triggered IS 'Whether compensation has been triggered';
    COMMENT ON COLUMN saga.payment_processing_saga_state.compensation_completed_at_utc IS 'UTC timestamp when compensation completed';
    COMMENT ON COLUMN saga.payment_processing_saga_state.authorization_timeout_token_id IS 'Token ID for authorization timeout scheduler - set when schedule is active';
    COMMENT ON COLUMN saga.payment_processing_saga_state.capture_approval_timeout_token_id IS 'Token ID for capture-approval wait-state timeout scheduler - set when schedule is active';
    COMMENT ON COLUMN saga.payment_processing_saga_state.capture_timeout_token_id IS 'Token ID for capture timeout scheduler - set when schedule is active';
    COMMENT ON COLUMN saga.payment_processing_saga_state.void_timeout_token_id IS 'Token ID for void timeout scheduler - set when schedule is active';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260602215520_CreateSagaTables') THEN
    CREATE INDEX ix_checkout_saga_state_current_state ON saga.checkout_saga_state (current_state);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260602215520_CreateSagaTables') THEN
    CREATE INDEX ix_checkout_saga_state_order_id ON saga.checkout_saga_state (order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260602215520_CreateSagaTables') THEN
    CREATE INDEX ix_checkout_saga_state_state_created ON saga.checkout_saga_state (current_state, created_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260602215520_CreateSagaTables') THEN
    CREATE INDEX ix_checkout_saga_state_state_last_updated ON saga.checkout_saga_state (current_state, last_modified_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260602215520_CreateSagaTables') THEN
    CREATE INDEX ix_checkout_saga_state_user_id ON saga.checkout_saga_state (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260602215520_CreateSagaTables') THEN
    CREATE INDEX ix_payment_processing_saga_state_current_state ON saga.payment_processing_saga_state (current_state);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260602215520_CreateSagaTables') THEN
    CREATE INDEX ix_payment_processing_saga_state_order_id ON saga.payment_processing_saga_state (order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260602215520_CreateSagaTables') THEN
    CREATE INDEX ix_payment_processing_saga_state_state_created ON saga.payment_processing_saga_state (current_state, created_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260602215520_CreateSagaTables') THEN
    CREATE INDEX ix_payment_processing_saga_state_state_last_updated ON saga.payment_processing_saga_state (current_state, last_modified_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260602215520_CreateSagaTables') THEN
    CREATE INDEX ix_payment_processing_saga_state_user_id ON saga.payment_processing_saga_state (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260602215520_CreateSagaTables') THEN
    CREATE UNIQUE INDEX ux_payment_processing_saga_state_idempotency_key ON saga.payment_processing_saga_state (idempotency_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260602215520_CreateSagaTables') THEN
    INSERT INTO saga."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260602215520_CreateSagaTables', '10.0.8');
    END IF;
END $EF$;
