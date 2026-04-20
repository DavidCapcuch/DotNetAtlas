using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Platform.ServiceDefaults.Json;

/// <summary>
/// Serializes <see cref="DateTimeOffset"/> values as ISO 8601 round-trip (<c>"O"</c>) strings with
/// explicit offset — e.g. <c>2026-04-19T14:30:00.0000000+02:00</c>. Implements ADR-0015 at the
/// JSON boundary so timestamps never lose offset on the wire.
/// </summary>
public sealed class JsonDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    /// <inheritdoc/>
    public override DateTimeOffset Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var raw = reader.GetString()
            ?? throw new JsonException("Expected non-null ISO 8601 DateTimeOffset string.");

        return DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    /// <inheritdoc/>
    public override void Write(
        Utf8JsonWriter writer,
        DateTimeOffset value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
    }
}
