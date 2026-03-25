using Microsoft.EntityFrameworkCore;

namespace Platform.ReliableMessaging.Outbox.EFCore.Common;

public static class ModelBuilderExtensions
{
    public static ModelBuilder ConfigureOutbox(
        this ModelBuilder modelBuilder,
        string? schemaName = null,
        string tableName = "OutboxMessages")
    {
        var schema = schemaName ?? modelBuilder.Model.GetDefaultSchema() ?? "dbo";
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration(schema, tableName));
        return modelBuilder;
    }
}
