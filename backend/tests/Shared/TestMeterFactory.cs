using System.Diagnostics.Metrics;

namespace OldCountry.Tests.Shared;

/// <summary>A meter factory for tests that build a class with <c>new</c>. The meters record to no exporter.</summary>
internal sealed class TestMeterFactory : IMeterFactory
{
    public Meter Create(MeterOptions options) => new(options);

    public void Dispose() { }
}
