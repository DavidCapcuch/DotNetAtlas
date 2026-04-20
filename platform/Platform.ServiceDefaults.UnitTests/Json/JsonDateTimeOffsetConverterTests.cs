using System.Text.Encodings.Web;
using System.Text.Json;
using Platform.ServiceDefaults.Json;

namespace Platform.ServiceDefaults.UnitTests.Json;

public class JsonDateTimeOffsetConverterTests
{
    // UnsafeRelaxedJsonEscaping leaves '+' unescaped so assertions can read the offset directly.
    // Production callers pick their own encoder; the converter itself emits "O"-format strings verbatim.
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    }.ConfigurePlatformJsonOptions();

    [Fact]
    public void Write_ProducesIso8601WithPositiveOffset()
    {
        // Arrange
        var value = new DateTimeOffset(2026, 4, 19, 14, 30, 0, TimeSpan.FromHours(2));

        // Act
        var json = JsonSerializer.Serialize(value, Options);

        // Assert
        using (new AssertionScope())
        {
            json.Should().StartWith("\"2026-04-19T14:30:00");
            json.Should().EndWith("+02:00\"");
        }
    }

    [Fact]
    public void Write_ProducesIso8601WithUtcZero()
    {
        // Arrange
        var value = new DateTimeOffset(2026, 4, 19, 14, 30, 0, TimeSpan.Zero);

        // Act
        var json = JsonSerializer.Serialize(value, Options);

        // Assert
        json.Should().EndWith("+00:00\"");
    }

    [Fact]
    public void Read_RoundTripsOffsetFromUtc()
    {
        // Arrange
        var original = new DateTimeOffset(2026, 4, 19, 14, 30, 0, 500, TimeSpan.Zero);

        // Act
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<DateTimeOffset>(json, Options);

        // Assert
        deserialized.Should().Be(original);
    }

    [Fact]
    public void Read_RoundTripsOffsetFromNonZero()
    {
        // Arrange
        var original = new DateTimeOffset(2026, 4, 19, 14, 30, 0, 500, TimeSpan.FromHours(-5));

        // Act
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<DateTimeOffset>(json, Options);

        // Assert
        using (new AssertionScope())
        {
            deserialized.Should().Be(original);
            deserialized.Offset.Should().Be(TimeSpan.FromHours(-5));
        }
    }

    [Fact]
    public void RoundTrip_MaintainsTicksPrecision()
    {
        // ADR-0015 "O" format preserves 100-ns ticks; the Risks section notes that a downstream Avro
        // serializer will truncate to microseconds, but the JSON boundary must not. Construct a value
        // with non-zero sub-millisecond ticks and assert byte-exact round-trip.
        var original = new DateTimeOffset(2026, 4, 19, 14, 30, 0, TimeSpan.FromHours(2)).AddTicks(1234567);

        // Act
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<DateTimeOffset>(json, Options);

        // Assert
        using (new AssertionScope())
        {
            deserialized.Ticks.Should().Be(original.Ticks);
            deserialized.Offset.Should().Be(original.Offset);
        }
    }

    [Fact]
    public void ConfigurePlatformJsonOptions_IsIdempotent()
    {
        // Arrange
        var options = new JsonSerializerOptions();

        // Act
        options.ConfigurePlatformJsonOptions().ConfigurePlatformJsonOptions();

        // Assert
        options.Converters.OfType<JsonDateTimeOffsetConverter>().Should().ContainSingle();
    }
}
