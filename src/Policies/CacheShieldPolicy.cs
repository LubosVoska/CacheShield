using System;

namespace CacheShield
{
    public sealed class CacheShieldPolicy
    {
        public TimeSpan? SoftTtl { get; set; }
        public TimeSpan? HardTtl { get; set; }
        public TimeSpan? MaxStaleOnFailure { get; set; }
        public TimeSpan? EarlyRefreshWindow { get; set; }
        public double? ExpirationJitterFraction { get; set; }
        public TimeSpan? LockWaitTimeout { get; set; }
        public long? MaxPayloadBytes { get; set; }
        public bool? SkipCachingNullOrDefault { get; set; }
    }
}
