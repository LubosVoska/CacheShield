using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace CacheShield
{
    internal sealed class KeyLockPool
    {
        private readonly ConcurrentDictionary<string, Entry> _locks = new ConcurrentDictionary<string, Entry>();
        private readonly TimeSpan _evictionWindow;
        private readonly Timer _sweeper;

        // Shared singleton with a reasonable default eviction window
        internal static readonly KeyLockPool Shared = new KeyLockPool(TimeSpan.FromMinutes(2));

        private KeyLockPool(TimeSpan evictionWindow)
        {
            _evictionWindow = evictionWindow <= TimeSpan.Zero ? TimeSpan.FromMinutes(2) : evictionWindow;
            // Sweep roughly once per eviction window; minimum of 30 seconds
            var period = _evictionWindow < TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : _evictionWindow;
            _sweeper = new Timer(Sweep, null, period, period);
        }

        internal Entry Rent(string key)
        {
            var entry = _locks.GetOrAdd(key, _ => new Entry());
            Interlocked.Increment(ref entry.RefCount);
            entry.Touch();
            return entry;
        }

        internal void Return(string key, Entry entry)
        {
            if (Interlocked.Decrement(ref entry.RefCount) == 0)
            {
                // Evict immediately if it has been idle long enough
                var last = Volatile.Read(ref entry.LastUsedTicks);
                if (DateTime.UtcNow.Ticks - last >= _evictionWindow.Ticks)
                {
                    // Remove only if the current mapping matches the same Entry instance
                    var pair = new KeyValuePair<string, Entry>(key, entry);
                    if (((ICollection<KeyValuePair<string, Entry>>)_locks).Remove(pair))
                    {
                        entry.Dispose();
                    }
                }
            }
        }

        private void Sweep(object? _)
        {
            var nowTicks = DateTime.UtcNow.Ticks;
            foreach (var kvp in _locks)
            {
                var entry = kvp.Value;
                if (Volatile.Read(ref entry.RefCount) == 0)
                {
                    var last = Volatile.Read(ref entry.LastUsedTicks);
                    if (nowTicks - last >= _evictionWindow.Ticks)
                    {
                        // Remove only if the current mapping matches the same Entry instance
                        var pair = new KeyValuePair<string, Entry>(kvp.Key, kvp.Value);
                        if (((ICollection<KeyValuePair<string, Entry>>)_locks).Remove(pair))
                        {
                            entry.Dispose();
                        }
                    }
                }
            }
        }

        internal sealed class Entry : IDisposable
        {
            internal readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1, 1);
            internal int RefCount;
            internal long LastUsedTicks;

            internal void Touch() => Volatile.Write(ref LastUsedTicks, DateTime.UtcNow.Ticks);

            public void Dispose() => Semaphore.Dispose();
        }
    }
}