using Basket.Application.Common.Messaging;
using Basket.Infrastructure.Messaging.Kafka.Config;
using Basket.Infrastructure.Persistence.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using Platform.ReliableMessaging.Outbox.EFCore.Common;

namespace Basket.Infrastructure.Common;

/// <summary>
/// Messaging DI for the Basket bounded context. Wires the producer-side
/// outbox (so the transactional outbox + <c>outbox-relay-basket</c> can publish
/// <c>BasketCheckoutInitiatedEvent</c> on <c>basket.sessions</c>) and the
/// inbox adapter against <see cref="BasketDbContext"/> so a future
/// Catalog price-invalidation consumer can dedupe without further wiring.
/// Basket has no Kafka consumers today — the publish path is 100% through the
/// outbox; consumer wiring can be added later if a use case warrants it.
/// </summary>
internal static class MessagingDependencyInjection
{
    /// <summary>
    /// Service origin identifier written to the <c>origin</c> Kafka header by
    /// the outbox relay (and by any future producer-headers middleware).
    /// </summary>
    internal const string KafkaProducerOrigin = "Basket";

    internal static IServiceCollection AddKafkaMessaging(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<KafkaOptions>()
            .BindConfiguration(KafkaOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<TopicsOptions>()
            .BindConfiguration(TopicsOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<SchemaRegistryOptions>()
            .BindConfiguration(SchemaRegistryOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<AvroSerializerOptions>()
            .BindConfiguration(AvroSerializerOptions.Section)
            .ValidateDataAnnotations();

        services.AddInbox<BasketDbContext>();

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
