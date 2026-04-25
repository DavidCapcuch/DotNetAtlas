using Inventory.Infrastructure.Messaging.Kafka.Config;
using Inventory.Infrastructure.Persistence.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using Platform.ReliableMessaging.Outbox.EFCore.Common;

namespace Inventory.Infrastructure.Common;

/// <summary>
/// Messaging DI for the Inventory bounded context. M4 wires the producer-side
/// outbox (so the transactional outbox + outbox-relay-inventory can publish
/// external events) and the inbox adapter against <c>InventoryDbContext</c>
/// so future Kafka consumers can dedupe saga commands. Kafka consumer
/// registration + KafkaFlow cluster wiring land in M5.
/// </summary>
internal static class MessagingDependencyInjection
{
    /// <summary>
    /// Service origin identifier written to the <c>origin</c> Kafka header by
    /// the outbox relay (and by the M5 producer-headers middleware).
    /// </summary>
    internal const string KafkaProducerOrigin = "Inventory";

    internal static IServiceCollection AddMessaging(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<KafkaOptions>()
            .BindConfiguration(KafkaOptions.Section)
            .ValidateDataAnnotations();

        services.AddInbox<InventoryDbContext>();

        services.AddOutbox(outbox =>
        {
            outbox.ConfigureMessageOrigin(KafkaProducerOrigin);

            outbox.ConfigureAvroSerializerConfig(options =>
            {
                configuration.Bind(AvroSerializerOptions.Section, options);
            });

            outbox.ConfigureSchemaRegistryConfig(options =>
            {
                configuration.Bind(SchemaRegistryOptions.Section, options);
            });
        });

        return services;
    }
}
