namespace SagaOrchestrators.Checkout.CheckoutSaga.Consumers;

/// <summary>
/// Avro timestamp helpers for Checkout consumer adapters. Avrogen 1.12 emits
/// <c>System.DateTime</c> for the <c>timestamp-millis</c> logical type; the
/// Confluent Avro deserialiser populates the value with <c>DateTimeKind.Utc</c>.
/// Internal saga events all use <see cref="DateTimeOffset"/> per ADR-0015, so
/// every consumer adapter routes timestamps through <see cref="ToUtcDateTimeOffset"/>
/// to make the conversion explicit and defensive against future Kind drift -
/// same pattern as <c>services/Ordering/.../SagaCommandMappers.ToOffset</c> and
/// <c>services/Inventory/.../SagaCommandMappers.ToOffset</c>.
/// </summary>
internal static class AvroTimestampExtensions
{
    /// <summary>
    /// Converts an Avro <c>timestamp-millis</c> <see cref="DateTime"/> into a
    /// <see cref="DateTimeOffset"/> anchored to UTC (offset 0).
    /// </summary>
    public static DateTimeOffset ToUtcDateTimeOffset(this DateTime utcDateTime) =>
        new(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), TimeSpan.Zero);
}
