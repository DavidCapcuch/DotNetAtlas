using System.Diagnostics.Metrics;
using EShop.BFF.Infrastructure.Common.Observability;

namespace EShop.BFF.IntegrationTests.Common;

/// <summary>
/// Per-test <see cref="MeterListener"/> that sums <c>long</c> measurements on the named
/// <see cref="BffMetrics"/> instruments, keeping only those tagged <c>bff.endpoint = endpoint</c>. The
/// static <c>EShop.BFF</c> meter is process-global, so this endpoint filter isolates a test from the
/// parallel collection exercising the other endpoint (which tags a different <c>bff.endpoint</c>).
/// </summary>
internal sealed class BffEndpointCounters : IDisposable
{
    private readonly MeterListener _listener;
    private readonly string _endpoint;
    private readonly Dictionary<string, long> _totals = new();
    private readonly Lock _gate = new();

    public BffEndpointCounters(string endpoint, params string[] instrumentNames)
    {
        _endpoint = endpoint;
        var names = instrumentNames.ToHashSet();
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == BffMetrics.MeterName && names.Contains(instrument.Name))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (!HasEndpointTag(tags))
            {
                return;
            }

            lock (_gate)
            {
                _totals[instrument.Name] = _totals.GetValueOrDefault(instrument.Name) + measurement;
            }
        });
        _listener.Start();
    }

    public long Total(string instrumentName)
    {
        lock (_gate)
        {
            return _totals.GetValueOrDefault(instrumentName);
        }
    }

    public void Dispose() => _listener.Dispose();

    private bool HasEndpointTag(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == BffMetrics.EndpointTag && (string?)tag.Value == _endpoint)
            {
                return true;
            }
        }

        return false;
    }
}
