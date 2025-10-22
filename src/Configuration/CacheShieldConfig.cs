using System;

namespace CacheShield
{
    public sealed class CacheShieldConfig
    {
        public ISerializer Serializer { get; set; } = new MessagePackSerializerWrapper();
        public TimeSpan DefaultHardTtl { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan DefaultSoftTtl { get; set; } = TimeSpan.FromMinutes(2);
        public double ExpirationJitterFraction { get; set; } = 0.0;
        public string? KeyPrefix { get; set; }
        public TimeSpan KeyLockEvictionWindow { get; set; } = TimeSpan.FromMinutes(2);
        public long? MaxPayloadBytes { get; set; }
        public bool SkipCachingNullOrDefault { get; set; } = false;
        public TimeSpan? LockWaitTimeout { get; set; }
    }

    public static class CacheShield
    {
        private static CacheShieldConfig _config = new CacheShieldConfig();
        public static CacheShieldConfig Config => _config;
        public static void Configure(Action<CacheShieldConfig> configure)
        {
            if (configure is null) throw new ArgumentNullException(nameof(configure));
            var cfg = new CacheShieldConfig();
            configure(cfg);
            _config = cfg;
            KeyLockPool.ConfigureShared(cfg.KeyLockEvictionWindow);
        }
    }
}
