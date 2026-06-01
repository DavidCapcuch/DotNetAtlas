using Microsoft.EntityFrameworkCore;

namespace Platform.ReliableMessaging.Inbox.EFCore.Common;

public static class ModelBuilderExtensions
{
    public static ModelBuilder ConfigureInbox(
        this ModelBuilder modelBuilder,
        string? schemaName = null,
        string tableName = "inbox_messages")
    {
        var schema = schemaName
            ?? modelBuilder.Model.GetDefaultSchema()
            ?? throw new InvalidOperationException(
                "No schema configured for the inbox table: pass a schemaName or set a model default schema.");
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration(schema, tableName));
        return modelBuilder;
    }
}
