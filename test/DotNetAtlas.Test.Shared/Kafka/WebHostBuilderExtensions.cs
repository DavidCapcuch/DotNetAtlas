using DotNetAtlas.Infrastructure.Communication.Kafka.Config;
using Microsoft.AspNetCore.Hosting;

namespace DotNetAtlas.Test.Shared.Kafka;

public static class WebHostBuilderExtensions
{
    public static IWebHostBuilder RegisterKafkaOptions(this IWebHostBuilder webBuilder, KafkaOptions kafkaOptions)
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
