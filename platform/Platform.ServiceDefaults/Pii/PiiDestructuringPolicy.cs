using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Platform.SharedKernel.Pii;
using Serilog.Core;
using Serilog.Events;

namespace Platform.ServiceDefaults.Pii;

/// <summary>
/// Serilog destructuring policy that redacts any value whose runtime type is marked with
/// <see cref="PiiAttribute"/> to the literal string <c>"***"</c> (ADR-0011).
/// </summary>
/// <remarks>
/// <para>
/// Applies to destructured captures only (<c>log.LogInformation("... {@Prop}", prop)</c>).
/// Raw primitive parameters (<c>log.LogInformation("... {Email}", email)</c>) are not wrapped —
/// that surface is the Wave 1 architecture-test concern per ADR-0011.
/// </para>
/// <para>
/// Wired into <see cref="Serilog.LoggerConfiguration"/> from
/// <see cref="Logging.SerilogSetup"/> as the single non-opt-in PII surface in Wave 0 M3 —
/// PII redaction in logs is foundational and must apply to every service that picks up
/// <c>AddServiceDefaults()</c>.
/// </para>
/// </remarks>
public sealed class PiiDestructuringPolicy : IDestructuringPolicy
{
    private static readonly ConcurrentDictionary<Type, bool> MarkerCache = new();
    private static readonly ScalarValue RedactedScalar = new("***");

    /// <inheritdoc />
    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        if (value is null)
        {
            result = null;
            return false;
        }

        if (!MarkerCache.GetOrAdd(value.GetType(), static t => t.IsDefined(typeof(PiiAttribute), inherit: true)))
        {
            result = null;
            return false;
        }

        result = RedactedScalar;
        return true;
    }
}
