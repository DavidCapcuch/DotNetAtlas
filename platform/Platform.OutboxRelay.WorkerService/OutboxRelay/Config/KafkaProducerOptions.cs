using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace Platform.OutboxRelay.WorkerService.OutboxRelay.Config;

/// <summary>
/// Kafka producer configuration for outbox relay.
/// </summary>
/// <remarks>
/// Every librdkafka producer setting is bindable by its <see cref="ProducerConfig"/> property name
/// (<c>MessageTimeoutMs</c>, not <c>message.timeout.ms</c>) without being redeclared here — and both
/// ways of getting that wrong fail silently, so <c>Platform.OutboxRelay.WorkerService.UnitTests</c>
/// pins them. A key naming no real setting binds to nothing and is discarded without an error,
/// leaving librdkafka on its own default. Redeclaring a setting with <c>new</c> writes a CLR backing
/// field, whereas <see cref="ProducerBuilder{TKey,TValue}"/> enumerates the base string dictionary;
/// the reflection binder populates the shadow and the hidden base property alike, so the values do
/// still arrive — until a binder that reads only declared members (the configuration-binding source
/// generator, trimming, AOT) leaves that dictionary empty.
/// <para>
/// Recommended read: https://github.com/confluentinc/confluent-kafka-dotnet/wiki/Producer.
/// </para>
/// </remarks>
public sealed class KafkaProducerOptions : ProducerConfig
{
    public const string Section = "KafkaProducer";

    public KafkaProducerOptions()
    {
    }

    private KafkaProducerOptions(IDictionary<string, string> settings)
        : base(settings)
    {
    }

    /// <summary>
    /// Copies the settings into an independent instance, so a caller that overrides one setting does
    /// not mutate the options singleton every other producer resolves.
    /// </summary>
    internal KafkaProducerOptions Clone() =>
        new(this.ToDictionary(setting => setting.Key, setting => setting.Value));
}

/// <summary>
/// Startup validation for <see cref="KafkaProducerOptions"/> (run via
/// <c>AddOptionsWithValidateOnStart</c>). These five settings must be supplied explicitly: the relay
/// refuses to start rather than connect or publish under a setting nobody stated. Idempotence must
/// additionally be <c>true</c>, because it is the one setting librdkafka will not catch — it rejects
/// every other contradictory combination when it builds the producer (<c>Acks</c> below <c>All</c>,
/// more than five in-flight, retries disabled), but accepts idempotence simply switched off, which
/// costs that partition its de-duplication and ordering with nothing said.
/// </summary>
internal sealed class KafkaProducerOptionsValidator : IValidateOptions<KafkaProducerOptions>
{
    public ValidateOptionsResult Validate(string? name, KafkaProducerOptions options)
    {
        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.BootstrapServers))
        {
            failures.Add(Missing(nameof(options.BootstrapServers)));
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add(Missing(nameof(options.ClientId)));
        }

        if (options.Acks is null)
        {
            failures.Add(Missing(nameof(options.Acks)));
        }

        if (options.EnableIdempotence is null)
        {
            failures.Add(Missing(nameof(options.EnableIdempotence)));
        }
        else if (options.EnableIdempotence is false)
        {
            failures.Add(Error(
                $"{nameof(options.EnableIdempotence)} must be true. The relay redelivers on retry, so " +
                "without it a broker-side retry duplicates and reorders that partition's events."));
        }

        if (options.CompressionType is null)
        {
            failures.Add(Missing(nameof(options.CompressionType)));
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }

    private static string Missing(string setting) => Error($"{setting} is required.");

    private static string Error(string problem) =>
        $"Kafka producer configuration error in section '{KafkaProducerOptions.Section}': {problem}";
}
