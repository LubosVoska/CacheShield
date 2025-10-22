using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace CacheShield
{
    internal sealed class KeyLockPool
    {
        private readonly ConcurrentDictionary<string, Entry> _locks = new ConcurrentDictionary<string, Entry>();
        private TimeSpan _evictionWindow;
        private Timer _sweeper;

        private static KeyLockPool s_shared = new KeyLockPool(TimeSpan.FromMinutes(2));
        internal static KeyLockPool Shared => Volatile.Read(ref s_shared);

        private KeyLockPool(TimeSpan evictionWindow)
        {
            _evictionWindow = evictionWindow <= TimeSpan.Zero ? TimeSpan.FromMinutes(2) : evictionWindow;
            var period = _evictionWindow < TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : _evictionWindow;
            _sweeper = new Timer(Sweep, null, period, period);
        }

        internal static void ConfigureShared(TimeSpan evictionWindow)
        {
            if (evictionWindow <= TimeSpan.Zero) evictionWindow = TimeSpan.FromMinutes(2);
            var replacement = new KeyLockPool(evictionWindow);
            var old = Interlocked.Exchange(ref s_shared, replacement);
            old.Dispose();
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
                var last = Volatile.Read(ref entry.LastUsedTicks);
                if (DateTime.UtcNow.Ticks - last >= _evictionWindow.Ticks)
                {
                    var pair = new KeyValuePair<string, Entry>(key, entry);
                    if (((ICollection<KeyValuePair<string, Entry>>)_locks).Remove(pair))
                    {
                        entry.Dispose();
                    }
                }
            }
        }

        private void Sweep(object _)
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
                        var pair = new KeyValuePair<string, Entry>(kvp.Key, kvp.Value);
                        if (((ICollection<KeyValuePair<string, Entry>>)_locks).Remove(pair))
                        {
                            entry.Dispose();
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            _sweeper.Dispose();
            foreach (var kv in _locks)
            {
                kv.Value.Dispose();
            }
            _locks.Clear();
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
