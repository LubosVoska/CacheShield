using System;

namespace CacheShield
{
    // Must be public for MessagePack Contractless resolver to build a formatter
    public readonly struct CacheEnvelope<T>
    {
        public CacheEnvelope(T value, DateTimeOffset softExpireUtc)
        {
            Value = value;
            SoftExpireUtc = softExpireUtc;
        }

        public T Value { get; }
        public DateTimeOffset SoftExpireUtc { get; }
    }
}
