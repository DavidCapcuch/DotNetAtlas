BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260131153625_Asdf'
)
BEGIN
    DECLARE @description AS sql_variant;
    SET @description = N'When the last paid subscription ended. Null if never had paid subscription.';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'weather', 'TABLE', N'AlertSubscribers', 'COLUMN', N'LastPaidSubscriptionEndedAtUtc';
END;

IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260131153625_Asdf'
)
BEGIN
    INSERT INTO [weather].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260131153625_Asdf', N'10.0.0');
END;

COMMIT;
GO

