using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using BusinessCard.Application.Observability;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCard.UnitTests.Support;

/// <summary>
/// Real <see cref="BusinessCardMetrics"/> plus a listener scoped to its own meter factory,
/// so parallel tests never see each other's measurements.
/// </summary>
internal sealed class MetricRecorder : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly MeterListener _listener = new();

    public MetricRecorder()
    {
        _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = _services.GetRequiredService<IMeterFactory>();
        Metrics = new BusinessCardMetrics(factory);

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter.Scope, factory))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.Start();
    }

    public BusinessCardMetrics Metrics { get; }

    public ConcurrentQueue<(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags)> Measurements { get; } = new();

    public IEnumerable<(double Value, IReadOnlyDictionary<string, object?> Tags)> For(string instrument) =>
        Measurements.Where(m => m.Instrument == instrument).Select(m => (m.Value, m.Tags));

    public void Dispose()
    {
        _listener.Dispose();
        _services.Dispose();
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        Measurements.Enqueue((instrument.Name, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value)));
}
