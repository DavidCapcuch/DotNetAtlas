using System.Text;
using Confluent.Kafka;

namespace Platform.OutboxRelay.WorkerService.IntegrationTests;

internal static class ConsumeResultHeaderExtensions
{
    /// <summary>
    /// Reads a header the relay wrote, as the UTF-8 string it was encoded from, or null when absent.
    /// </summary>
    public static string? ReadHeader<TValue>(this ConsumeResult<string, TValue> result, string key) =>
        result.Message.Headers.TryGetLastBytes(key, out var value)
            ? Encoding.UTF8.GetString(value)
            : null;
}
