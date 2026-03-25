using Microsoft.EntityFrameworkCore;

namespace Platform.ReliableMessaging.Inbox.EFCore.Common;

public static class ModelBuilderExtensions
{
    public static ModelBuilder ConfigureInbox(
        this ModelBuilder modelBuilder,
        string? schemaName = null,
        string tableName = "InboxMessages")
    {
        var schema = schemaName ?? modelBuilder.Model.GetDefaultSchema() ?? "dbo";
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration(schema, tableName));
        return modelBuilder;
    }
}
