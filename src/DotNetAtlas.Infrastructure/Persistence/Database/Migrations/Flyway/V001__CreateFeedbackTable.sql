IF OBJECT_ID(N'[weather].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'weather') IS NULL EXEC(N'CREATE SCHEMA [weather];');
    CREATE TABLE [weather].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251026190419_CreateFeedbackTable'
)
BEGIN
    IF SCHEMA_ID(N'weather') IS NULL EXEC(N'CREATE SCHEMA [weather];');
END;

IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251026190419_CreateFeedbackTable'
)
BEGIN
    CREATE TABLE [weather].[Feedbacks] (
        [Id] uniqueidentifier NOT NULL,
        [CreatedByUser] uniqueidentifier NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [LastModifiedUtc] datetime2 NOT NULL,
        [Feedback] nvarchar(500) NOT NULL,
        [Rating] int NOT NULL,
        [Timestamp] rowversion NULL,
        CONSTRAINT [PK_Feedbacks] PRIMARY KEY ([Id])
    );
    DECLARE @description AS sql_variant;
    SET @description = N'Contains user feedbacks about the weather.';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'weather', 'TABLE', N'Feedbacks';
    SET @description = N'PK';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'weather', 'TABLE', N'Feedbacks', 'COLUMN', N'Id';
    SET @description = N'User who created the feedback.';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'weather', 'TABLE', N'Feedbacks', 'COLUMN', N'CreatedByUser';
    SET @description = N'Creation timestamp (UTC).';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'weather', 'TABLE', N'Feedbacks', 'COLUMN', N'CreatedUtc';
    SET @description = N'Last modification timestamp (UTC).';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'weather', 'TABLE', N'Feedbacks', 'COLUMN', N'LastModifiedUtc';
    SET @description = N'Weather feedback from the user.';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'weather', 'TABLE', N'Feedbacks', 'COLUMN', N'Feedback';
    SET @description = N'Rating given by the user.';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'weather', 'TABLE', N'Feedbacks', 'COLUMN', N'Rating';
    SET @description = N'Optimistic concurrency token.';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'weather', 'TABLE', N'Feedbacks', 'COLUMN', N'Timestamp';
END;

IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251026190419_CreateFeedbackTable'
)
BEGIN
    CREATE UNIQUE INDEX [UX_WeatherFeedback_CreatedByUser] ON [weather].[Feedbacks] ([CreatedByUser]);
END;

IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251026190419_CreateFeedbackTable'
)
BEGIN
    INSERT INTO [weather].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251026190419_CreateFeedbackTable', N'10.0.0-rc.1.25451.107');
END;

COMMIT;
GO

