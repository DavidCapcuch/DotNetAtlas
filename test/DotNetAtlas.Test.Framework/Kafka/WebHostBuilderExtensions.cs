using Microsoft.AspNetCore.Hosting;
using Weather.Infrastructure.Messaging.Kafka.Config;

namespace DotNetAtlas.Test.Framework.Kafka;

public static class WebHostBuilderExtensions
{
    public static IWebHostBuilder UseKafkaSettings(this IWebHostBuilder webBuilder, KafkaOptions kafkaOptions)
    {
        for (var i = 0; i < kafkaOptions.Brokers.Length; i++)
        {
            webBuilder.UseSetting($"{KafkaOptions.Section}:Brokers:{i}", kafkaOptions.Brokers[i]);
        }

        webBuilder.UseSetting($"{SchemaRegistryOptions.Section}:Url", kafkaOptions.SchemaRegistry.Url);
        webBuilder.UseSetting($"{AvroSerializerOptions.Section}:AutoRegisterSchemas",
            kafkaOptions.AvroSerializer.AutoRegisterSchemas.ToString());
        webBuilder.UseSetting($"{AvroSerializerOptions.Section}:SubjectNameStrategy",
            kafkaOptions.AvroSerializer.SubjectNameStrategy.ToString());

        return webBuilder;
    }
}
