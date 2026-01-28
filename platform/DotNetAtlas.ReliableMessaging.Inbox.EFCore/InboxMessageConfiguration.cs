using DotNetAtlas.ReliableMessaging.Inbox.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetAtlas.ReliableMessaging.Inbox.EFCore;

/// <summary>
/// Entity configuration for the InboxMessage entity.
/// Configures the inbox pattern table for idempotent message processing.
/// </summary>
public class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    private readonly string _schemaName;
    private readonly string _tableName;

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxMessageConfiguration"/> class.
    /// </summary>
    /// <param name="schemaName">The database schema name.</param>
    /// <param name="tableName">The table name. Defaults to "InboxMessages".</param>
    public InboxMessageConfiguration(string schemaName = "dbo", string tableName = "InboxMessages")
    {
        _schemaName = schemaName;
        _tableName = tableName;
    }

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable(_tableName, _schemaName, t => t.HasComment(
            "Inbox pattern table for idempotent message processing. Tracks processed messages to prevent duplicate processing."));

        builder.HasKey(i => i.MessageId);

        builder.Property(i => i.MessageId)
            .HasComment("Unique message identifier (Primary Key).")
            .ValueGeneratedNever();

        builder.Property(i => i.ProcessedAtUtc)
            .HasComment("UTC timestamp when the message was processed.");

        builder.HasIndex(i => i.ProcessedAtUtc)
            .HasDatabaseName($"IX_{_tableName}_ProcessedAtUtc");
    }
}
