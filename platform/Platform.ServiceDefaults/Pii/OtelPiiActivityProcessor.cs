using System.Collections.Concurrent;
using System.Diagnostics;
using OpenTelemetry;
using Platform.SharedKernel.Pii;

namespace Platform.ServiceDefaults.Pii;

/// <summary>
/// OpenTelemetry <see cref="BaseProcessor{T}"/> that walks each emitted activity's
/// tag values and redacts any whose runtime type carries <see cref="PiiAttribute"/>
/// to the literal string <c>"***"</c> (ADR-0011). Parity with
/// <see cref="PiiDestructuringPolicy"/> on the Serilog side — both surfaces are
/// driven by the same attribute marker so PII-tagged value-object types do not
/// leak into Jaeger / Seq exporters when set as span attributes.
/// </summary>
/// <remarks>
/// <para>
/// Lookup is cached per runtime type (concurrent dictionary) so the per-activity
/// cost is constant after warm-up.
/// </para>
/// <para>
/// Property-level <see cref="PiiAttribute"/> on a primitive tag value (e.g., a
/// string property flagged at the source) is undetectable at this layer — the
/// runtime tag only carries the boxed value, not its source property. Call-site
/// masking remains required for those (see e.g. PaymentTransactionResponseMapper).
/// </para>
/// </remarks>
public sealed class OtelPiiActivityProcessor : BaseProcessor<Activity>
{
    private static readonly ConcurrentDictionary<Type, bool> MarkerCache = new();
    private const string Redacted = "***";

    /// <inheritdoc />
    public override void OnEnd(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        foreach (var kv in activity.TagObjects)
        {
            if (kv.Value is null)
            {
                continue;
            }

            var isPii = MarkerCache.GetOrAdd(
                kv.Value.GetType(),
                static t => t.IsDefined(typeof(PiiAttribute), inherit: true));

            if (isPii)
            {
                activity.SetTag(kv.Key, Redacted);
            }
        }
    }
}
