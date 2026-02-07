IF OBJECT_ID(N'[saga].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'saga') IS NULL EXEC(N'CREATE SCHEMA [saga];');
    CREATE TABLE [saga].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    IF SCHEMA_ID(N'saga') IS NULL EXEC(N'CREATE SCHEMA [saga];');
END;

IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    CREATE TABLE [saga].[AlertSubscriptionExtensionSagaState] (
        [CorrelationId] uniqueidentifier NOT NULL,
        [CurrentState] nvarchar(64) NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [PaymentMethodId] uniqueidentifier NOT NULL,
        [DurationDays] int NOT NULL,
        [Amount] decimal(19,4) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [IdempotencyKey] nvarchar(128) NOT NULL,
        [PaymentTransactionId] uniqueidentifier NULL,
        [ExtensionInitiatedAtUtc] datetimeoffset NOT NULL,
        [PaymentCompletedAtUtc] datetimeoffset NULL,
        [CreatedUtc] datetimeoffset NOT NULL,
        [LastModifiedUtc] datetimeoffset NOT NULL,
        [ExtensionCompletedAtUtc] datetimeoffset NULL,
        [NewExpiresAtUtc] datetimeoffset NULL,
        [ErrorMessage] nvarchar(2048) NULL,
        [ErrorCode] nvarchar(64) NULL,
        [CompensationTriggered] bit NOT NULL,
        [CompensationCompletedAtUtc] datetimeoffset NULL,
        [RowVersion] rowversion NULL,
        [PaymentTimeoutTokenId] uniqueidentifier NULL,
        [ExtensionTimeoutTokenId] uniqueidentifier NULL,
        [CompensationTimeoutTokenId] uniqueidentifier NULL,
        CONSTRAINT [PK_AlertSubscriptionExtensionSagaState] PRIMARY KEY ([CorrelationId])
    );
    DECLARE @description AS sql_variant;
    SET @description = N'Saga state for alert subscription extension orchestration.';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState';
    SET @description = N'PK - Unique correlation ID (also PaymentTransactionId)';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'CorrelationId';
    SET @description = N'Current state of the saga state machine';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'CurrentState';
    SET @description = N'User who is extending the subscription';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'UserId';
    SET @description = N'ID of the saved payment method';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'PaymentMethodId';
    SET @description = N'Subscription extension duration in days';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'DurationDays';
    SET @description = N'Payment amount';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'Amount';
    SET @description = N'ISO 4217 currency code';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'Currency';
    SET @description = N'Idempotency key to prevent duplicate extensions';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'IdempotencyKey';
    SET @description = N'Payment transaction ID (set after payment completes)';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'PaymentTransactionId';
    SET @description = N'UTC timestamp when extension was initiated';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'ExtensionInitiatedAtUtc';
    SET @description = N'UTC timestamp when payment completed (null if not completed)';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'PaymentCompletedAtUtc';
    SET @description = N'UTC timestamp when saga was created';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'CreatedUtc';
    SET @description = N'UTC timestamp when saga was last updated';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'LastModifiedUtc';
    SET @description = N'UTC timestamp when extension completed (null if not completed)';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'ExtensionCompletedAtUtc';
    SET @description = N'New subscription expiration date after extension (null if not completed)';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'NewExpiresAtUtc';
    SET @description = N'Error message if failed';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'ErrorMessage';
    SET @description = N'Error code for categorized failure handling';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'ErrorCode';
    SET @description = N'Whether compensation (refund) has been triggered';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'CompensationTriggered';
    SET @description = N'UTC timestamp when compensation completed';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'CompensationCompletedAtUtc';
    SET @description = N'Optimistic concurrency token.';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'RowVersion';
    SET @description = N'Token ID for payment timeout scheduler - set when schedule is active';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'PaymentTimeoutTokenId';
    SET @description = N'Token ID for extension timeout scheduler - set when schedule is active';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'ExtensionTimeoutTokenId';
    SET @description = N'Token ID for compensation timeout scheduler - set when schedule is active';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionExtensionSagaState', 'COLUMN', N'CompensationTimeoutTokenId';
END;

IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    CREATE TABLE [saga].[AlertSubscriptionPurchaseSagaState] (
        [CorrelationId] uniqueidentifier NOT NULL,
        [CurrentState] nvarchar(64) NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [PaymentMethodId] uniqueidentifier NOT NULL,
        [SubscriptionTier] int NOT NULL,
        [DurationDays] int NOT NULL,
        [Amount] decimal(19,4) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [IdempotencyKey] nvarchar(128) NOT NULL,
        [PaymentTransactionId] uniqueidentifier NULL,
        [PurchaseInitiatedUtc] datetimeoffset NOT NULL,
        [PaymentCompletedUtc] datetimeoffset NULL,
        [CreatedUtc] datetimeoffset NOT NULL,
        [LastModifiedUtc] datetimeoffset NOT NULL,
        [ActivationCompletedUtc] datetimeoffset NULL,
        [ErrorMessage] nvarchar(2048) NULL,
        [ErrorCode] nvarchar(64) NULL,
        [CompensationTriggered] bit NOT NULL,
        [CompensationCompletedUtc] datetimeoffset NULL,
        [RowVersion] rowversion NULL,
        [PaymentTimeoutTokenId] uniqueidentifier NULL,
        [ActivationTimeoutTokenId] uniqueidentifier NULL,
        [CompensationTimeoutTokenId] uniqueidentifier NULL,
        CONSTRAINT [PK_AlertSubscriptionPurchaseSagaState] PRIMARY KEY ([CorrelationId])
    );
    DECLARE @description1 AS sql_variant;
    SET @description1 = N'Saga state for alert subscription purchase orchestration.';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState';
    SET @description1 = N'PK - Unique correlation ID (also PaymentTransactionId)';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'CorrelationId';
    SET @description1 = N'Current state of the saga state machine';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'CurrentState';
    SET @description1 = N'User who purchased the subscription';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'UserId';
    SET @description1 = N'ID of the saved payment method';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'PaymentMethodId';
    SET @description1 = N'Subscription tier (Pro, Ultra)';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'SubscriptionTier';
    SET @description1 = N'Subscription duration in days';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'DurationDays';
    SET @description1 = N'Payment amount';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'Amount';
    SET @description1 = N'ISO 4217 currency code';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'Currency';
    SET @description1 = N'Idempotency key to prevent duplicate purchases';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'IdempotencyKey';
    SET @description1 = N'Payment transaction ID (set after payment completes)';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'PaymentTransactionId';
    SET @description1 = N'UTC timestamp when purchase was initiated';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'PurchaseInitiatedUtc';
    SET @description1 = N'UTC timestamp when payment completed (null if not completed)';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'PaymentCompletedUtc';
    SET @description1 = N'UTC timestamp when saga was created';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'CreatedUtc';
    SET @description1 = N'UTC timestamp when saga was last updated';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'LastModifiedUtc';
    SET @description1 = N'UTC timestamp when activation completed (null if not completed)';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'ActivationCompletedUtc';
    SET @description1 = N'Error message if failed';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'ErrorMessage';
    SET @description1 = N'Error code for categorized failure handling';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'ErrorCode';
    SET @description1 = N'Whether compensation (refund) has been triggered';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'CompensationTriggered';
    SET @description1 = N'UTC timestamp when compensation completed';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'CompensationCompletedUtc';
    SET @description1 = N'Optimistic concurrency token.';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'RowVersion';
    SET @description1 = N'Token ID for payment timeout scheduler - set when schedule is active';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'PaymentTimeoutTokenId';
    SET @description1 = N'Token ID for activation timeout scheduler - set when schedule is active';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'ActivationTimeoutTokenId';
    SET @description1 = N'Token ID for compensation timeout scheduler - set when schedule is active';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'saga', 'TABLE', N'AlertSubscriptionPurchaseSagaState', 'COLUMN', N'CompensationTimeoutTokenId';
END;

IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    CREATE TABLE [saga].[OutboxMessages] (
        [Id] bigint NOT NULL IDENTITY,
        [TopicName] varchar(249) NOT NULL,
        [KafkaKey] nvarchar(128) NULL,
        [AvroPayload] varbinary(max) NOT NULL,
        [Type] varchar(255) NOT NULL,
        [Headers] nvarchar(max) NULL,
        [CreatedUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_OutboxMessages] PRIMARY KEY ([Id])
    );
    DECLARE @description2 AS sql_variant;
    SET @description2 = N'Outbox pattern table for storing domain events as Avro-serialized messages for reliable event publishing.';
    EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', N'saga', 'TABLE', N'OutboxMessages';
    SET @description2 = N'PK, Identity';
    EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', N'saga', 'TABLE', N'OutboxMessages', 'COLUMN', N'Id';
    SET @description2 = N'The Kafka topic where this message will be published. Set by the message producer.';
    EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', N'saga', 'TABLE', N'OutboxMessages', 'COLUMN', N'TopicName';
    SET @description2 = N'Kafka Key - typically the Aggregate ID for proper event ordering and partitioning';
    EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', N'saga', 'TABLE', N'OutboxMessages', 'COLUMN', N'KafkaKey';
    SET @description2 = N'Avro-serialized domain event payload';
    EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', N'saga', 'TABLE', N'OutboxMessages', 'COLUMN', N'AvroPayload';
    SET @description2 = N'Avro type name of the serialized event (e.g., ''FeedbackChangedEvent'') for deserialization and observability';
    EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', N'saga', 'TABLE', N'OutboxMessages', 'COLUMN', N'Type';
    SET @description2 = N'JSON dictionary of OpenTelemetry-standard headers for distributed tracing and metadata. Headers are automatically generated by OpenTelemetry propagators for end-to-end trace context propagation.';
    EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', N'saga', 'TABLE', N'OutboxMessages', 'COLUMN', N'Headers';
    SET @description2 = N'Creation timestamp (UTC).';
    EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', N'saga', 'TABLE', N'OutboxMessages', 'COLUMN', N'CreatedUtc';
END;

IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    CREATE TABLE [saga].[PaymentProcessingSagaState] (
        [CorrelationId] uniqueidentifier NOT NULL,
        [CurrentState] nvarchar(64) NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [PaymentMethodId] uniqueidentifier NOT NULL,
        [Amount] decimal(19,4) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [IdempotencyKey] nvarchar(128) NOT NULL,
        [AuthorizationId] nvarchar(256) NULL,
        [AuthorizationExpiresAtUtc] datetimeoffset NULL,
        [PaymentTransactionId] uniqueidentifier NULL,
        [InitiatedAtUtc] datetimeoffset NOT NULL,
        [CreatedUtc] datetimeoffset NOT NULL,
        [LastModifiedUtc] datetimeoffset NOT NULL,
        [AuthorizedAtUtc] datetimeoffset NULL,
        [CapturedAtUtc] datetimeoffset NULL,
        [AuthorizationRetryCount] int NOT NULL DEFAULT 0,
        [CaptureRetryCount] int NOT NULL DEFAULT 0,
        [ErrorCode] nvarchar(64) NULL,
        [ErrorMessage] nvarchar(2048) NULL,
        [CompensationTriggered] bit NOT NULL,
        [CompensationCompletedAtUtc] datetimeoffset NULL,
        [RowVersion] rowversion NULL,
        [AuthorizationTimeoutTokenId] uniqueidentifier NULL,
        [CaptureTimeoutTokenId] uniqueidentifier NULL,
        [VoidTimeoutTokenId] uniqueidentifier NULL,
        [RefundTimeoutTokenId] uniqueidentifier NULL,
        [SuccessFinalizationTimeoutTokenId] uniqueidentifier NULL,
        CONSTRAINT [PK_PaymentProcessingSagaState] PRIMARY KEY ([CorrelationId])
    );
    DECLARE @description3 AS sql_variant;
    SET @description3 = N'Saga state for payment processing orchestration.';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState';
    SET @description3 = N'Unique correlation ID for the payment saga';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'CorrelationId';
    SET @description3 = N'Current state of the saga state machine';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'CurrentState';
    SET @description3 = N'User initiating the payment';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'UserId';
    SET @description3 = N'ID of the saved payment method';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'PaymentMethodId';
    SET @description3 = N'Payment amount';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'Amount';
    SET @description3 = N'ISO 4217 currency code';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'Currency';
    SET @description3 = N'Idempotency key to prevent duplicate processing';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'IdempotencyKey';
    SET @description3 = N'Authorization ID from payment provider';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'AuthorizationId';
    SET @description3 = N'UTC timestamp when authorization expires';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'AuthorizationExpiresAtUtc';
    SET @description3 = N'Payment transaction ID after capture';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'PaymentTransactionId';
    SET @description3 = N'UTC timestamp when payment was initiated';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'InitiatedAtUtc';
    SET @description3 = N'UTC timestamp when saga was created';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'CreatedUtc';
    SET @description3 = N'UTC timestamp when saga was last updated';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'LastModifiedUtc';
    SET @description3 = N'UTC timestamp when authorization completed';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'AuthorizedAtUtc';
    SET @description3 = N'UTC timestamp when capture completed';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'CapturedAtUtc';
    SET @description3 = N'Number of authorization retry attempts';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'AuthorizationRetryCount';
    SET @description3 = N'Number of capture retry attempts';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'CaptureRetryCount';
    SET @description3 = N'Error code for categorized failure handling';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'ErrorCode';
    SET @description3 = N'Error message if failed';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'ErrorMessage';
    SET @description3 = N'Whether compensation has been triggered';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'CompensationTriggered';
    SET @description3 = N'UTC timestamp when compensation completed';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'CompensationCompletedAtUtc';
    SET @description3 = N'Optimistic concurrency token.';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'RowVersion';
    SET @description3 = N'Token ID for authorization timeout scheduler - set when schedule is active';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'AuthorizationTimeoutTokenId';
    SET @description3 = N'Token ID for capture timeout scheduler - set when schedule is active';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'CaptureTimeoutTokenId';
    SET @description3 = N'Token ID for void timeout scheduler - set when schedule is active';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'VoidTimeoutTokenId';
    SET @description3 = N'Token ID for refund timeout scheduler - set when schedule is active';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'RefundTimeoutTokenId';
    SET @description3 = N'Token ID for success finalization timeout scheduler - set when schedule is active';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'saga', 'TABLE', N'PaymentProcessingSagaState', 'COLUMN', N'SuccessFinalizationTimeoutTokenId';
END;

IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    CREATE INDEX [IX_SubscriptionExtensionSagaState_CurrentState] ON [saga].[AlertSubscriptionExtensionSagaState] ([CurrentState]);
END;

IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SubscriptionExtensionSagaState_IdempotencyKey] ON [saga].[AlertSubscriptionExtensionSagaState] ([IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    CREATE INDEX [IX_SubscriptionExtensionSagaState_State_Created] ON [saga].[AlertSubscriptionExtensionSagaState] ([CurrentState], [CreatedUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    CREATE INDEX [IX_SubscriptionExtensionSagaState_State_LastUpdated] ON [saga].[AlertSubscriptionExtensionSagaState] ([CurrentState], [LastModifiedUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    CREATE INDEX [IX_SubscriptionExtensionSagaState_UserId] ON [saga].[AlertSubscriptionExtensionSagaState] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    CREATE INDEX [IX_SubscriptionPurchaseSagaState_CurrentState] ON [saga].[AlertSubscriptionPurchaseSagaState] ([CurrentState]);
END;

IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SubscriptionPurchaseSagaState_IdempotencyKey] ON [saga].[AlertSubscriptionPurchaseSagaState] ([IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    CREATE INDEX [IX_SubscriptionPurchaseSagaState_State_Created] ON [saga].[AlertSubscriptionPurchaseSagaState] ([CurrentState], [CreatedUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    CREATE INDEX [IX_SubscriptionPurchaseSagaState_State_LastUpdated] ON [saga].[AlertSubscriptionPurchaseSagaState] ([CurrentState], [LastModifiedUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    CREATE INDEX [IX_SubscriptionPurchaseSagaState_UserId] ON [saga].[AlertSubscriptionPurchaseSagaState] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    CREATE INDEX [IX_PaymentSagaState_CurrentState] ON [saga].[PaymentProcessingSagaState] ([CurrentState]);
END;

IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PaymentSagaState_IdempotencyKey] ON [saga].[PaymentProcessingSagaState] ([IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    CREATE INDEX [IX_PaymentSagaState_State_Created] ON [saga].[PaymentProcessingSagaState] ([CurrentState], [CreatedUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    CREATE INDEX [IX_PaymentSagaState_State_LastUpdated] ON [saga].[PaymentProcessingSagaState] ([CurrentState], [LastModifiedUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    CREATE INDEX [IX_PaymentSagaState_UserId] ON [saga].[PaymentProcessingSagaState] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [saga].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260207112951_Initial'
)
BEGIN
    INSERT INTO [saga].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260207112951_Initial', N'10.0.0');
END;

COMMIT;
GO

