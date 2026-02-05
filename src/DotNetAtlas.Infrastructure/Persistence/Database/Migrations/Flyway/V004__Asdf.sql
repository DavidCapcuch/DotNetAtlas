BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260204220526_Asdf'
)
BEGIN
    DECLARE @description AS sql_variant;
    EXEC sp_dropextendedproperty 'MS_Description', 'SCHEMA', N'weather', 'TABLE', N'OutboxMessages', 'COLUMN', N'Type';
    SET @description = N'Avro type name of the serialized event (e.g., ''FeedbackChangedEvent'') for deserialization and observability';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'weather', 'TABLE', N'OutboxMessages', 'COLUMN', N'Type';
END;

IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260204220526_Asdf'
)
BEGIN
    ALTER TABLE [weather].[OutboxMessages] ADD [TopicName] varchar(249) NOT NULL DEFAULT '';
    DECLARE @description1 AS sql_variant;
    SET @description1 = N'The Kafka topic where this message will be published. Set by the message producer.';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'weather', 'TABLE', N'OutboxMessages', 'COLUMN', N'TopicName';
END;

IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260204220526_Asdf'
)
BEGIN
    DECLARE @description2 AS sql_variant;
    SET @description2 = N'When the last paid subscription ended. Null if never had paid subscription.';
    EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', N'weather', 'TABLE', N'AlertSubscribers', 'COLUMN', N'LastPaidSubscriptionEndedAtUtc';
END;

IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260204220526_Asdf'
)
BEGIN
    INSERT INTO [weather].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260204220526_Asdf', N'10.0.0');
END;

COMMIT;
GO

