using System.Collections.Frozen;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using OpenTelemetry;

namespace Platform.ServiceDefaults.Pii;

/// <summary>
/// OpenTelemetry processor that strips span attributes not on a positive allowlist (ADR-0011).
/// </summary>
/// <remarks>
/// <para>
/// The processor runs <see cref="OnEnd"/> on every completed <see cref="Activity"/>; any tag
/// whose key is not in the hard-coded default allowlist, not in one of the allowed prefixes,
/// and not in the caller-configured <see cref="PiiAllowlistOptions.AdditionalAttributes"/> /
/// <see cref="PiiAllowlistOptions.AdditionalPrefixes"/> is removed by calling
/// <see cref="Activity.SetTag(string, object?)"/> with a <c>null</c> value (OTel's remove idiom).
/// </para>
/// <para>
/// Default exact-match allowlist (ADR-0011 line 98): <c>http.method</c>, <c>http.status_code</c>,
/// <c>http.route</c>, <c>rpc.service</c>, <c>messaging.destination.name</c>,
/// <c>messaging.kafka.consumer.group</c>, <c>db.system</c>, <c>db.name</c>, <c>correlation.id</c>,
/// <c>order.id</c>, <c>payment.id</c>, <c>invoice.id</c>, <c>buyer.id.hash</c>.
/// </para>
/// <para>
/// Default prefix-match allowlist keeps standard OTel-instrumentation namespaces so new tags
/// from upstream libraries aren't silently dropped as they evolve: <c>http.</c>, <c>messaging.</c>,
/// <c>db.</c>, <c>rpc.</c>, <c>net.</c>, <c>url.</c>, <c>server.</c>, <c>client.</c>, <c>otel.</c>,
/// <c>exception.</c>.
/// </para>
/// </remarks>
public sealed class PiiAllowlistProcessor : BaseProcessor<Activity>
{
    private static readonly FrozenSet<string> DefaultAttributes = new[]
    {
        "http.method",
        "http.status_code",
        "http.route",
        "rpc.service",
        "messaging.destination.name",
        "messaging.kafka.consumer.group",
        "db.system",
        "db.name",
        "correlation.id",
        "order.id",
        "payment.id",
        "invoice.id",
        "buyer.id.hash",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly string[] DefaultPrefixes =
    [
        "http.",
        "messaging.",
        "db.",
        "rpc.",
        "net.",
        "url.",
        "server.",
        "client.",
        "otel.",
        "exception.",
    ];

    private readonly IOptionsMonitor<PiiAllowlistOptions> _options;

    /// <summary>
    /// Creates a new processor. Typically resolved from DI via
    /// <see cref="PiiAllowlistTracerProviderBuilderExtensions.AddPiiAllowlistProcessor"/>.
    /// </summary>
    public PiiAllowlistProcessor(IOptionsMonitor<PiiAllowlistOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public override void OnEnd(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var options = _options.CurrentValue;

        // Collect keys to drop first — cannot mutate while iterating TagObjects.
        List<string>? toDrop = null;
        foreach (var tag in data.TagObjects)
        {
            if (IsAllowed(tag.Key, options))
            {
                continue;
            }

            toDrop ??= [];
            toDrop.Add(tag.Key);
        }

        if (toDrop is null)
        {
            return;
        }

        foreach (var key in toDrop)
        {
            data.SetTag(key, null);
        }
    }

    private static bool IsAllowed(string key, PiiAllowlistOptions options)
    {
        if (DefaultAttributes.Contains(key))
        {
            return true;
        }

        foreach (var prefix in DefaultPrefixes)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var extra in options.AdditionalAttributes)
        {
            if (string.Equals(key, extra, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var prefix in options.AdditionalPrefixes)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
