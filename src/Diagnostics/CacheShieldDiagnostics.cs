#if NETSTANDARD2_1
namespace CacheShield
{
 internal static class CacheShieldDiagnostics
 {
 internal static readonly NoopCounter Hits = new NoopCounter();
 internal static readonly NoopCounter Misses = new NoopCounter();
 internal static readonly NoopCounter StaleServed = new NoopCounter();
 internal static readonly NoopCounter RefreshStarted = new NoopCounter();
 internal static readonly NoopCounter RefreshCompleted = new NoopCounter();
 internal static readonly NoopCounter DeserializationFailures = new NoopCounter();
 internal static readonly NoopHistogram LockWaitMs = new NoopHistogram();
 internal static readonly NoopHistogram ComputeMs = new NoopHistogram();

 internal sealed class NoopCounter { public void Add(long value) { } }
 internal sealed class NoopHistogram { public void Record(double value) { } }
 }
}
#else
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CacheShield
{
    internal static class CacheShieldDiagnostics
    {
        internal static readonly ActivitySource ActivitySource = new("CacheShield");
        internal static readonly Meter Meter = new("CacheShield");

        internal static readonly Counter<long> Hits = Meter.CreateCounter<long>("cacheshield.hits");
        internal static readonly Counter<long> Misses = Meter.CreateCounter<long>("cacheshield.misses");
        internal static readonly Counter<long> StaleServed = Meter.CreateCounter<long>("cacheshield.stale_served");
        internal static readonly Counter<long> RefreshStarted = Meter.CreateCounter<long>("cacheshield.refresh_started");
        internal static readonly Counter<long> RefreshCompleted = Meter.CreateCounter<long>("cacheshield.refresh_completed");
        internal static readonly Counter<long> DeserializationFailures = Meter.CreateCounter<long>("cacheshield.deserialize_failures");
        internal static readonly Histogram<double> LockWaitMs = Meter.CreateHistogram<double>("cacheshield.lock_wait_ms");
        internal static readonly Histogram<double> ComputeMs = Meter.CreateHistogram<double>("cacheshield.compute_ms");
    }
}
#endif
