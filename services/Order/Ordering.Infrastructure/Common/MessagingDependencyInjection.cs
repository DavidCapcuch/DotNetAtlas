using DotNetAtlas.ReliableMessaging.Outbox.EFCore.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Application.Common.Messaging;
using Ordering.Application.Common.Observability;
using Ordering.Infrastructure.Common.Config.Kafka;

namespace Ordering.Infrastructure.Common;

/// <summary>
/// Dependency injection extensions for communication infrastructure.
/// The Ordering service is a producer-only service — it publishes order initiation events
/// via the transactional outbox pattern but does not consume Kafka messages.
/// </summary>
internal static class MessagingDependencyInjection
{
    /// <summary>
    /// Configures Kafka messaging with outbox-only publishing (no consumers).
    /// The Ordering service publishes AlertSubscriptionPurchaseInitiatedEvent and
    /// AlertSubscriptionExtensionInitiatedEvent to the order.alert-subscriptions topic.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration manager.</param>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection AddKafkaMessaging(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<TopicsOptions>()
            .BindConfiguration(TopicsOptions.Section)
            .ValidateDataAnnotations();

        services.AddOutbox(outbox =>
        {
            outbox.ConfigureMessageOrigin(ApplicationInfo.AppName);

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
