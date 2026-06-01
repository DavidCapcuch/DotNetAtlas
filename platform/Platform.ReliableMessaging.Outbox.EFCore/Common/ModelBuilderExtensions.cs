using Microsoft.EntityFrameworkCore;

namespace Platform.ReliableMessaging.Outbox.EFCore.Common;

public static class ModelBuilderExtensions
{
    public static ModelBuilder ConfigureOutbox(
        this ModelBuilder modelBuilder,
        string? schemaName = null,
        string tableName = "outbox_messages")
    {
        var schema = schemaName
            ?? modelBuilder.Model.GetDefaultSchema()
            ?? throw new InvalidOperationException(
                "No schema configured for the outbox table: pass a schemaName or set a model default schema.");
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration(schema, tableName));
        return modelBuilder;
    }
}
