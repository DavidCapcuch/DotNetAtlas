using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Reflection;
using Avro.Specific;
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Schema = Avro.Schema;

namespace DotNetAtlas.Sagas.Common.AvroDeserialization;

/// <summary>
/// Automatic deserializer for any ISpecificRecord type.
/// Auto-detects the message type from the schema ID in the Avro bytes and caches
/// deserializers at runtime for performance.
/// </summary>
/// <remarks>
/// <para>
/// Expects a serialization format matching Confluent.SchemaRegistry.Serdes.AvroSerializer:
/// <list type="bullet">
///   <item>Byte 0: Magic byte (0x00) used to identify the protocol format.</item>
///   <item>Bytes 1-4: Unique global id of the Avro schema (big endian), as registered in Confluent Schema Registry.</item>
///   <item>Remaining bytes: The serialized Avro data.</item>
/// </list>
/// </para>
/// <para>
/// Assembly scanning is performed once per AppDomain on first instantiation. All assemblies loaded
/// at that time are scanned for ISpecificRecord implementations. Assemblies loaded after the initial
/// scan will not be included unless <see cref="RegisterAssembly"/> is called explicitly.
/// </para>
/// </remarks>
public class UniversalAvroDeserializer : IAsyncDeserializer<ISpecificRecord>
{
    /// <summary>
    /// Magic byte used by Confluent Schema Registry wire format.
    /// </summary>
    private const byte ConfluentWireFormatMagicByte = 0;

    /// <summary>
    /// Minimum data length required for Confluent wire format (1 magic byte + 4 schema ID bytes).
    /// </summary>
    private const int MinimumWireFormatLength = 5;

    private readonly ISchemaRegistryClient _schemaRegistryClient;
    private readonly AvroDeserializerConfig? _avroDeserializerConfig;
    private readonly ConcurrentDictionary<int, IAsyncDeserializer<ISpecificRecord>> _deserializersBySchemaId = new();

    private static readonly ConcurrentDictionary<string, Type> TypeRegistry = new();
    private static readonly Lock ScanLock = new();
    private static bool _typesScanned;

    /// <summary>
    /// Initializes a new instance of the <see cref="UniversalAvroDeserializer"/> class.
    /// </summary>
    /// <param name="schemaRegistryClient">The schema registry client for fetching schemas.</param>
    /// <param name="avroDeserializerConfig">Optional Avro deserializer configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="schemaRegistryClient"/> is null.</exception>
    public UniversalAvroDeserializer(
        ISchemaRegistryClient schemaRegistryClient,
        AvroDeserializerConfig? avroDeserializerConfig = null)
    {
        _schemaRegistryClient = schemaRegistryClient ?? throw new ArgumentNullException(nameof(schemaRegistryClient));
        _avroDeserializerConfig = avroDeserializerConfig;
        EnsureTypesScanned();
    }

    /// <summary>
    /// Deserializes Avro bytes to an ISpecificRecord, automatically detecting the type
    /// from the schema ID embedded in the message.
    /// </summary>
    /// <param name="data">The data to deserialize.</param>
    /// <param name="isNull">Whether the data is null.</param>
    /// <param name="context">The serialization context.</param>
    /// <returns>The deserialized record.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when the data is too short or has an invalid magic byte.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no ISpecificRecord type is found for the schema.
    /// </exception>
    public async Task<ISpecificRecord> DeserializeAsync(
        ReadOnlyMemory<byte> data,
        bool isNull,
        SerializationContext context)
    {
        if (isNull || data.Length == 0)
        {
            return null!;
        }

        var schemaId = ReadSchemaId(data.Span);
        var deserializer = await GetOrCreateDeserializerAsync(schemaId).ConfigureAwait(false);

        return await deserializer.DeserializeAsync(data, isNull, context).ConfigureAwait(false);
    }

    /// <summary>
    /// Registers additional assemblies for ISpecificRecord type discovery.
    /// Use this method to register assemblies that were loaded after the initial scan.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan for ISpecificRecord types.</param>
    public static void RegisterAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies)
        {
            RegisterAssembly(assembly);
        }
    }

    /// <summary>
    /// Registers an additional assembly for ISpecificRecord type discovery.
    /// Use this method to register assemblies that were loaded after the initial scan.
    /// </summary>
    /// <param name="assembly">The assembly to scan for ISpecificRecord types.</param>
    public static void RegisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ScanAssemblyForSpecificRecords(assembly);
    }

    private static int ReadSchemaId(ReadOnlySpan<byte> data)
    {
        if (data.Length < MinimumWireFormatLength)
        {
            throw new InvalidDataException(
                $"Expecting data framing of length {MinimumWireFormatLength} bytes or more " +
                $"but total data size is {data.Length} bytes.");
        }

        if (data[0] != ConfluentWireFormatMagicByte)
        {
            throw new InvalidDataException(
                $"Expecting data with Confluent Schema Registry framing. " +
                $"Magic byte was 0x{data[0]:X2}, expecting 0x{ConfluentWireFormatMagicByte:X2}.");
        }

        return BinaryPrimitives.ReadInt32BigEndian(data.Slice(1, 4));
    }

    private async Task<IAsyncDeserializer<ISpecificRecord>> GetOrCreateDeserializerAsync(int schemaId)
    {
        if (_deserializersBySchemaId.TryGetValue(schemaId, out var cached))
        {
            return cached;
        }

        var schema = await _schemaRegistryClient.GetSchemaAsync(schemaId).ConfigureAwait(false);
        var avroSchema = Schema.Parse(schema.SchemaString);
        var fullName = avroSchema.Fullname;

        if (!TypeRegistry.TryGetValue(fullName, out var messageType))
        {
            throw new InvalidOperationException(
                $"No ISpecificRecord type found for schema '{fullName}'. " +
                $"Ensure the assembly containing this type is loaded and was scanned. " +
                $"You can manually register assemblies using {nameof(RegisterAssembly)}.");
        }

        var deserializer = CreateTypedDeserializer(messageType);
        _deserializersBySchemaId.TryAdd(schemaId, deserializer);

        return deserializer;
    }

    private IAsyncDeserializer<ISpecificRecord> CreateTypedDeserializer(Type messageType)
    {
        var wrapperType = typeof(TypedDeserializerWrapper<>).MakeGenericType(messageType);
        return (IAsyncDeserializer<ISpecificRecord>)Activator.CreateInstance(
            wrapperType,
            _schemaRegistryClient,
            _avroDeserializerConfig)!;
    }

    private static void EnsureTypesScanned()
    {
        if (_typesScanned)
        {
            return;
        }

        lock (ScanLock)
        {
            if (_typesScanned)
            {
                return;
            }

            ScanAllLoadedAssemblies();
            _typesScanned = true;
        }
    }

    private static void ScanAllLoadedAssemblies()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            ScanAssemblyForSpecificRecords(assembly);
        }
    }

    private static void ScanAssemblyForSpecificRecords(Assembly assembly)
    {
        try
        {
            foreach (var type in assembly.GetTypes())
            {
                if (!typeof(ISpecificRecord).IsAssignableFrom(type) ||
                    type.IsAbstract ||
                    type.IsInterface)
                {
                    continue;
                }

                var schemaField = type.GetField("_SCHEMA", BindingFlags.Public | BindingFlags.Static);
                if (schemaField?.GetValue(null) is Schema schema)
                {
                    TypeRegistry.TryAdd(schema.Fullname, type);
                }
            }
        }
        catch (ReflectionTypeLoadException)
        {
            // Ignore assemblies that can't be fully loaded (e.g., missing dependencies)
        }
        catch (TypeLoadException)
        {
            // Ignore types that can't be loaded
        }
    }

    private sealed class TypedDeserializerWrapper<T> : IAsyncDeserializer<ISpecificRecord>
        where T : class, ISpecificRecord
    {
        private readonly AvroDeserializer<T> _inner;

        public TypedDeserializerWrapper(
            ISchemaRegistryClient schemaRegistryClient,
            AvroDeserializerConfig? avroDeserializerConfig)
        {
            _inner = new AvroDeserializer<T>(schemaRegistryClient, avroDeserializerConfig);
        }

        public async Task<ISpecificRecord> DeserializeAsync(
            ReadOnlyMemory<byte> data,
            bool isNull,
            SerializationContext context)
        {
            return await _inner.DeserializeAsync(data, isNull, context).ConfigureAwait(false);
        }
    }
}
