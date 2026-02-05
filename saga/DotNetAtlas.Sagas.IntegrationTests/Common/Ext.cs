using DotNetAtlas.Infrastructure.Messaging.Kafka.Config;
using DotNetAtlas.Sagas.Common.Config;
using Microsoft.AspNetCore.Hosting;

namespace DotNetAtlas.Sagas.IntegrationTests.Common;

public static class Ext
{
    public static IWebHostBuilder UseKafkaaSettings(this IWebHostBuilder webBuilder, KafkaOptions kafkaOptions)
    {
        for (var i = 0; i < kafkaOptions.Brokers.Length; i++)
        {
            webBuilder.UseSetting($"{SagaKafkaOptions.Section}:Brokers:{i}", kafkaOptions.Brokers[i]);
        }

        webBuilder.UseSetting($"{SagaSchemaRegistryOptions.Section}:Url", kafkaOptions.SchemaRegistry.Url);
        webBuilder.UseSetting($"{AvroDeserializerOptions.Section}:SubjectNameStrategy",
            kafkaOptions.AvroSerializer.SubjectNameStrategy.ToString());

        return webBuilder;
    }
}
