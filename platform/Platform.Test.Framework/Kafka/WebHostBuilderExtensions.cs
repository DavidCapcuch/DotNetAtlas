using Microsoft.AspNetCore.Hosting;
using Platform.Test.Framework.Kafka.Config;

namespace Platform.Test.Framework.Kafka;

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

    public static IWebHostBuilder UseUnreachableKafkaSettings(this IWebHostBuilder webBuilder)
    {
        webBuilder.UseSetting($"{KafkaOptions.Section}:Brokers:0", "kafka-not-used-in-tests:9094");
        webBuilder.UseSetting($"{SchemaRegistryOptions.Section}:Url",
            "http://schema-registry-not-used-in-tests:8081");

        return webBuilder;
    }
}
