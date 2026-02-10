using System.Numerics;
using Avro;

namespace DotNetAtlas.SchemaRegistry.Contracts.Avro.AvroExtensions;

/// <summary>
/// Extension methods for converting .NET decimal values to Avro decimal format.
/// IMPORTANT: Never rely on implicit conversion to AvroDecimal. The .NET decimal value
/// might have a different scale than the AVSC schema definition, which will cause
/// serialization to fail if the scales don't match. Always use this explicit conversion
/// method to ensure the decimal is properly scaled according to your schema.
/// </summary>
public static class AvroDecimalExtensions
{
    /// <summary>
    /// Converts a .NET decimal to Avro.AvroDecimal with the specified scale.
    /// </summary>
    /// <param name="value">The decimal value to convert.</param>
    /// <param name="scale">The number of digits after the decimal point (must match your AVSC schema).</param>
    /// <returns>An Avro.AvroDecimal representation of the value with the correct scale.</returns>
    /// <remarks>
    /// Example AVSC schema with scale 4:
    /// <code>
    /// {"name": "amount", "type": {"type": "bytes", "logicalType": "decimal", "precision": 19, "scale": 4}}
    /// </code>
    /// 
    /// Without explicit conversion, this would fail serialization:
    /// <code>
    /// var exampleDecimal = 1.23m; // .NET decimal with scale 2
    /// var avroEvent = new ExampleAvro { Amount = exampleDecimal }; // Serialization fails!
    /// </code>
    /// 
    /// Correct usage:
    /// <code>
    /// var exampleDecimal = 1.23m;
    /// var avroEvent = new ExampleAvro { Amount = exampleDecimal.ToAvroDecimal(4) }; // Scales to 4: 1.2300
    /// </code>
    /// </remarks>
    public static AvroDecimal ToAvroDecimal(this decimal value, int scale)
    {
        var scalingFactor = BigInteger.Pow(new BigInteger(10), scale);

        var scaledValue = value * (decimal)scalingFactor;
        var unscaledInteger = new BigInteger(scaledValue);

        return new AvroDecimal(unscaledInteger, scale);
    }
}
