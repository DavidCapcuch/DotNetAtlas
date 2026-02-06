using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Reflection;
using Avro.Specific;
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Schema = Avro.Schema;

namespace DotNetAtlas.Avro.UniversalSerDes;

/// <summary>
/// Universal deserializer for Avro messages that automatically detects the message type at runtime.
/// </summary>
/// <remarks>
/// Based on https://github.com/ycherkes/multi-schema-avro-deserializer/blob/main/src/YCherkes.SchemaRegistry.Serdes.Avro/MultiSchemaAvroDeserializer.cs.
/// <para>
/// This deserializer reads the schema ID from the Confluent wire format header, fetches the corresponding
/// schema from the Schema Registry, and dynamically creates a typed deserializer for the matching
/// <see cref="ISpecificRecord"/> implementation.
/// </para>
/// <para>
/// Expects messages serialized with Confluent Schema Registry wire format:
/// <list type="bullet">
///   <item><description>Byte 0: Magic byte (0x00) identifying the Confluent wire format.</description></item>
///   <item><description>Bytes 1-4: Schema ID (big-endian int32) as registered in Confluent Schema Registry.</description></item>
///   <item><description>Bytes 5+: Avro-encoded payload.</description></item>
/// </list>
/// </para>
/// <para>
/// Type Discovery: On first instantiation, all currently loaded assemblies are scanned
/// for concrete <see cref="ISpecificRecord"/> implementations. Each type's <c>_SCHEMA</c> static field
/// is read to build a mapping from Avro schema full name to .NET type.
/// </para>
/// <para>
/// Late-Loaded Assemblies: Assemblies loaded after the initial scan are not automatically
/// included. Use <see cref="RegisterAssembly"/> or <see cref="RegisterAssemblies"/> to manually register
/// types from late-loaded assemblies.
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
    private readonly AvroDeserializerConfig _avroDeserializerConfig;
    private readonly ConcurrentDictionary<int, IAsyncDeserializer<ISpecificRecord>> _deserializersBySchemaId = new();

    private static readonly ConcurrentDictionary<string, Type> TypeRegistry = new();
    private static readonly Lock ScanLock = new();
    private static bool _typesScanned;
    private static ILogger _logger = NullLogger.Instance;

    /// <summary>
    /// Initializes a new instance of the <see cref="UniversalAvroDeserializer"/> class.
    /// </summary>
    /// <param name="schemaRegistryClient">The schema registry client for fetching schemas.</param>
    /// <param name="avroDeserializerConfig">The Avro deserializer configuration.</param>
    /// <param name="logger">Optional logger for diagnostics. If not provided, logging is disabled.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="schemaRegistryClient"/> or <paramref name="avroDeserializerConfig"/> is null.
    /// </exception>
    public UniversalAvroDeserializer(
        ISchemaRegistryClient schemaRegistryClient,
        AvroDeserializerConfig avroDeserializerConfig,
        ILogger<UniversalAvroDeserializer>? logger = null)
    {
        _schemaRegistryClient = schemaRegistryClient ?? throw new ArgumentNullException(nameof(schemaRegistryClient));
        _avroDeserializerConfig =
            avroDeserializerConfig ?? throw new ArgumentNullException(nameof(avroDeserializerConfig));

        if (logger != null)
        {
            _logger = logger;
        }

        EnsureTypesScanned();
    }

    /// <summary>
    /// Deserializes Avro bytes to an <see cref="ISpecificRecord"/>, automatically detecting the concrete
    /// type from the schema ID embedded in the message header.
    /// </summary>
    /// <param name="data">The raw message bytes in Confluent wire format.</param>
    /// <param name="isNull">Indicates whether the message value is null (tombstone message).</param>
    /// <param name="context">The Kafka serialization context containing topic and header information.</param>
    /// <returns>
    /// The deserialized record, or <c>null</c> if <paramref name="isNull"/> is <c>true</c>
    /// or <paramref name="data"/> is empty.
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when the data is shorter than 5 bytes (minimum wire format length) or when the magic byte
    /// is not 0x00 (Confluent wire format identifier).
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no <see cref="ISpecificRecord"/> implementation is found for the schema's full name.
    /// This typically indicates the assembly containing the generated Avro class was not loaded or scanned.
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
    /// Registers multiple assemblies for <see cref="ISpecificRecord"/> type discovery.
    /// </summary>
    /// <remarks>
    /// Use this method to register assemblies that were loaded after the initial scan (e.g., plugin assemblies
    /// or assemblies loaded via <see cref="System.Runtime.Loader.AssemblyLoadContext"/>).
    /// </remarks>
    /// <param name="assemblies">The assemblies to scan for <see cref="ISpecificRecord"/> implementations.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assemblies"/> is null.</exception>
    public static void RegisterAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies)
        {
            RegisterAssembly(assembly);
        }
    }

    /// <summary>
    /// Registers a single assembly for <see cref="ISpecificRecord"/> type discovery.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scans the assembly for all concrete types implementing <see cref="ISpecificRecord"/> and registers
    /// them in the type registry using their Avro schema full name as the key.
    /// </para>
    /// <para>
    /// If an assembly cannot be fully loaded (e.g., due to missing dependencies), the scan is skipped
    /// and a debug log message is emitted.
    /// </para>
    /// </remarks>
    /// <param name="assembly">The assembly to scan for <see cref="ISpecificRecord"/> implementations.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assembly"/> is null.</exception>
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
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        _logger.LogDebug(
            "Scanning {AssemblyCount} loaded assemblies for ISpecificRecord implementations",
            assemblies.Length);

        foreach (var assembly in assemblies)
        {
            ScanAssemblyForSpecificRecords(assembly);
        }

        _logger.LogDebug(
            "Assembly scan complete. Registered {TypeCount} ISpecificRecord types",
            TypeRegistry.Count);
    }

    private static void ScanAssemblyForSpecificRecords(Assembly assembly)
    {
        try
        {
            var registeredCount = 0;

            foreach (var type in assembly.GetTypes())
            {
                if (!typeof(ISpecificRecord).IsAssignableFrom(type) ||
                    type.IsAbstract ||
                    type.IsInterface)
                {
                    continue;
                }

                var schemaField = type.GetField("_SCHEMA", BindingFlags.Public | BindingFlags.Static);
                if (schemaField?.GetValue(null) is Schema schema && TypeRegistry.TryAdd(schema.Fullname, type))
                {
                    registeredCount++;
                    _logger.LogDebug(
                        "Registered ISpecificRecord type {TypeName} for schema {SchemaFullName}",
                        type.FullName, schema.Fullname);
                }
            }

            if (registeredCount > 0)
            {
                _logger.LogDebug(
                    "Found {Count} ISpecificRecord types in assembly {AssemblyName}",
                    registeredCount, assembly.GetName().Name);
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            _logger.LogDebug(
                ex,
                "Could not load all types from assembly {AssemblyName}. Some types may be missing from the registry",
                assembly.GetName().Name);
        }
        catch (TypeLoadException ex)
        {
            _logger.LogDebug(
                ex, "Could not load a type from assembly {AssemblyName}",
                assembly.GetName().Name);
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
