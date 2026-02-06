BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260119202905_AddAlertAndInboxTables'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[weather].[Feedbacks]') AND [c].[name] = N'Rating');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [weather].[Feedbacks] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [weather].[Feedbacks] ALTER COLUMN [Rating] tinyint NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260119202905_AddAlertAndInboxTables'
)
BEGIN
    CREATE TABLE [weather].[AlertSubscribers] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [SubscriptionTier] int NOT NULL,
        [SubscriptionExpiryAtUtc] datetimeoffset NULL,
        [LastPaidSubscriptionEndedAtUtc] datetimeoffset NULL,
        [TemperatureUnitPreference] int NOT NULL,
        [WindSpeedUnitPreference] int NOT NULL,
        [CreatedUtc] datetimeoffset NOT NULL,
        [LastModifiedUtc] datetimeoffset NOT NULL,
        [Timestamp] rowversion NULL,
        CONSTRAINT [PK_AlertSubscribers] PRIMARY KEY ([Id])
    );
    DECLARE @description1 AS sql_variant;
    SET @description1 = N'Contains subscribers for weather alert subscriptions.';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'weather', 'TABLE', N'AlertSubscribers';
    SET @description1 = N'PK';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'weather', 'TABLE', N'AlertSubscribers', 'COLUMN', N'Id';
    SET @description1 = N'User who subscribed for weather alerts.';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'weather', 'TABLE', N'AlertSubscribers', 'COLUMN', N'UserId';
    SET @description1 = N'Subscription tier (Free, Pro, Ultra).';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'weather', 'TABLE', N'AlertSubscribers', 'COLUMN', N'SubscriptionTier';
    SET @description1 = N'Expiry date for subscription (UTC). Null for free tier.';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'weather', 'TABLE', N'AlertSubscribers', 'COLUMN', N'SubscriptionExpiryAtUtc';
    SET @description1 = N'Preferred temperature unit (Celsius, Fahrenheit, Kelvin).';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'weather', 'TABLE', N'AlertSubscribers', 'COLUMN', N'TemperatureUnitPreference';
    SET @description1 = N'Preferred wind speed unit (KilometersPerHour, MilesPerHour).';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'weather', 'TABLE', N'AlertSubscribers', 'COLUMN', N'WindSpeedUnitPreference';
    SET @description1 = N'Timestamp when user first subscribed (UTC).';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'weather', 'TABLE', N'AlertSubscribers', 'COLUMN', N'CreatedUtc';
    SET @description1 = N'Timestamp when subscription was last modified (UTC).';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'weather', 'TABLE', N'AlertSubscribers', 'COLUMN', N'LastModifiedUtc';
    SET @description1 = N'Optimistic concurrency token.';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'weather', 'TABLE', N'AlertSubscribers', 'COLUMN', N'Timestamp';
END;

IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260119202905_AddAlertAndInboxTables'
)
BEGIN
    CREATE TABLE [weather].[InboxMessages] (
        [MessageId] uniqueidentifier NOT NULL,
        [ProcessedAtUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_InboxMessages] PRIMARY KEY ([MessageId])
    );
    DECLARE @description2 AS sql_variant;
    SET @description2 = N'Inbox pattern table for idempotent message processing. Tracks processed messages to prevent duplicate processing.';
    EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', N'weather', 'TABLE', N'InboxMessages';
    SET @description2 = N'Unique message identifier (Primary Key).';
    EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', N'weather', 'TABLE', N'InboxMessages', 'COLUMN', N'MessageId';
    SET @description2 = N'UTC timestamp when the message was processed.';
    EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', N'weather', 'TABLE', N'InboxMessages', 'COLUMN', N'ProcessedAtUtc';
END;

IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260119202905_AddAlertAndInboxTables'
)
BEGIN
    CREATE TABLE [weather].[Locations] (
        [Id] uniqueidentifier NOT NULL,
        [CountryCode] int NOT NULL,
        [CreatedUtc] datetimeoffset NOT NULL,
        [LastModifiedUtc] datetimeoffset NOT NULL,
        [City] nvarchar(100) NOT NULL,
        [Timestamp] rowversion NULL,
        CONSTRAINT [PK_Locations] PRIMARY KEY ([Id])
    );
    DECLARE @description3 AS sql_variant;
    SET @description3 = N'Contains city-country locations.';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'weather', 'TABLE', N'Locations';
    SET @description3 = N'PK';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'weather', 'TABLE', N'Locations', 'COLUMN', N'Id';
    SET @description3 = N'ISO 3166-1 alpha-2 country code.';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'weather', 'TABLE', N'Locations', 'COLUMN', N'CountryCode';
    SET @description3 = N'Creation timestamp (UTC).';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'weather', 'TABLE', N'Locations', 'COLUMN', N'CreatedUtc';
    SET @description3 = N'Last modification timestamp (UTC).';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'weather', 'TABLE', N'Locations', 'COLUMN', N'LastModifiedUtc';
    SET @description3 = N'Name of the city.';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'weather', 'TABLE', N'Locations', 'COLUMN', N'City';
    SET @description3 = N'Optimistic concurrency token.';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', N'weather', 'TABLE', N'Locations', 'COLUMN', N'Timestamp';
END;

IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260119202905_AddAlertAndInboxTables'
)
BEGIN
    CREATE TABLE [weather].[MonitoredLocationAlertsSubscriptions] (
        [Id] uniqueidentifier NOT NULL,
        [MonitoredLocationId] uniqueidentifier NOT NULL,
        [CreatedUtc] datetimeoffset NOT NULL,
        [LastModifiedUtc] datetimeoffset NOT NULL,
        [AlertSubscriberId] uniqueidentifier NULL,
        [Timestamp] rowversion NULL,
        CONSTRAINT [PK_MonitoredLocationAlertsSubscriptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MonitoredLocationAlertsSubscriptions_AlertSubscribers_AlertSubscriberId] FOREIGN KEY ([AlertSubscriberId]) REFERENCES [weather].[AlertSubscribers] ([Id]) ON DELETE NO ACTION
    );
    DECLARE @description4 AS sql_variant;
    SET @description4 = N'Contains user subscriptions to monitored location weather alerts.';
    EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', N'weather', 'TABLE', N'MonitoredLocationAlertsSubscriptions';
    SET @description4 = N'PK';
    EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', N'weather', 'TABLE', N'MonitoredLocationAlertsSubscriptions', 'COLUMN', N'Id';
    SET @description4 = N'FK to MonitoredLocation (ID reference only, no navigation).';
    EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', N'weather', 'TABLE', N'MonitoredLocationAlertsSubscriptions', 'COLUMN', N'MonitoredLocationId';
    SET @description4 = N'Creation timestamp (UTC).';
    EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', N'weather', 'TABLE', N'MonitoredLocationAlertsSubscriptions', 'COLUMN', N'CreatedUtc';
    SET @description4 = N'Last modification timestamp (UTC).';
    EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', N'weather', 'TABLE', N'MonitoredLocationAlertsSubscriptions', 'COLUMN', N'LastModifiedUtc';
    SET @description4 = N'Optimistic concurrency token.';
    EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', N'weather', 'TABLE', N'MonitoredLocationAlertsSubscriptions', 'COLUMN', N'Timestamp';
END;

IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260119202905_AddAlertAndInboxTables'
)
BEGIN
    CREATE TABLE [weather].[MonitoredLocations] (
        [Id] uniqueidentifier NOT NULL,
        [LocationId] uniqueidentifier NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedUtc] datetimeoffset NOT NULL,
        [LastModifiedUtc] datetimeoffset NOT NULL,
        [HighHumidityThresholdPercent] float NOT NULL,
        [HighTemperatureThresholdC] float NOT NULL,
        [HighWindSpeedThresholdKmh] float NOT NULL,
        [LowHumidityThresholdPercent] float NOT NULL,
        [LowTemperatureThresholdC] float NOT NULL,
        [Timestamp] rowversion NULL,
        [RecentReadings] nvarchar(max) NULL,
        CONSTRAINT [PK_MonitoredLocations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MonitoredLocations_Locations_LocationId] FOREIGN KEY ([LocationId]) REFERENCES [weather].[Locations] ([Id]) ON DELETE NO ACTION
    );
    DECLARE @description5 AS sql_variant;
    SET @description5 = N'Contains monitored locations with weather sensor data and alert thresholds.';
    EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', N'weather', 'TABLE', N'MonitoredLocations';
    SET @description5 = N'PK';
    EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', N'weather', 'TABLE', N'MonitoredLocations', 'COLUMN', N'Id';
    SET @description5 = N'Whether this location is actively being monitored.';
    EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', N'weather', 'TABLE', N'MonitoredLocations', 'COLUMN', N'IsActive';
    SET @description5 = N'Creation timestamp (UTC).';
    EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', N'weather', 'TABLE', N'MonitoredLocations', 'COLUMN', N'CreatedUtc';
    SET @description5 = N'Last modification timestamp (UTC).';
    EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', N'weather', 'TABLE', N'MonitoredLocations', 'COLUMN', N'LastModifiedUtc';
    SET @description5 = N'Humidity threshold for high humidity alerts (%).';
    EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', N'weather', 'TABLE', N'MonitoredLocations', 'COLUMN', N'HighHumidityThresholdPercent';
    SET @description5 = N'Temperature threshold for high temperature alerts (°C).';
    EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', N'weather', 'TABLE', N'MonitoredLocations', 'COLUMN', N'HighTemperatureThresholdC';
    SET @description5 = N'Wind speed threshold for high wind alerts (km/h).';
    EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', N'weather', 'TABLE', N'MonitoredLocations', 'COLUMN', N'HighWindSpeedThresholdKmh';
    SET @description5 = N'Humidity threshold for low humidity alerts (%).';
    EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', N'weather', 'TABLE', N'MonitoredLocations', 'COLUMN', N'LowHumidityThresholdPercent';
    SET @description5 = N'Temperature threshold for low temperature alerts (°C).';
    EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', N'weather', 'TABLE', N'MonitoredLocations', 'COLUMN', N'LowTemperatureThresholdC';
    SET @description5 = N'Optimistic concurrency token.';
    EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', N'weather', 'TABLE', N'MonitoredLocations', 'COLUMN', N'Timestamp';
END;

IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260119202905_AddAlertAndInboxTables'
)
BEGIN
    CREATE INDEX [IX_Subscribers_SubscriptionTier_ExpiryUtc] ON [weather].[AlertSubscribers] ([SubscriptionTier], [SubscriptionExpiryAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260119202905_AddAlertAndInboxTables'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Subscribers_UserId] ON [weather].[AlertSubscribers] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260119202905_AddAlertAndInboxTables'
)
BEGIN
    CREATE INDEX [IX_InboxMessages_ProcessedAtUtc] ON [weather].[InboxMessages] ([ProcessedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260119202905_AddAlertAndInboxTables'
)
BEGIN
    CREATE INDEX [IX_MonitoredLocationAlertsSubscriptions_AlertSubscriberId] ON [weather].[MonitoredLocationAlertsSubscriptions] ([AlertSubscriberId]);
END;

IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260119202905_AddAlertAndInboxTables'
)
BEGIN
    CREATE INDEX [IX_MonitoredLocationAlertsSubscriptions_MonitoredLocationId] ON [weather].[MonitoredLocationAlertsSubscriptions] ([MonitoredLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260119202905_AddAlertAndInboxTables'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MonitoredLocations_LocationId] ON [weather].[MonitoredLocations] ([LocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [weather].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260119202905_AddAlertAndInboxTables'
)
BEGIN
    INSERT INTO [weather].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260119202905_AddAlertAndInboxTables', N'10.0.0');
END;

COMMIT;
GO

