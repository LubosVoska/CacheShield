using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CacheShield
{
    /// <summary>
    /// Provides extension methods for <see cref="IDistributedCache"/> to prevent cache stampede issues.
    /// </summary>
    public static class DistributedCacheExtensions
    {
        // Dictionary to hold per-key semaphores
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new();

        /// <summary>
        /// Gets a value from cache, with a caller-supplied <paramref name="getMethod"/> (async, stateless) that is used if the value is not yet available
        /// </summary>
        public static async ValueTask<T> GetAsync<T>(this IDistributedCache cache, string key, Func<CancellationToken, ValueTask<T>> getMethod,
            DistributedCacheEntryOptions? options = null, CancellationToken cancellation = default)
            => await GetAsyncShared<int, T>(cache, key, state: 0, getMethod, options, cancellation).ConfigureAwait(false); // use dummy state

        /// <summary>
        /// Gets a value from cache, with a caller-supplied <paramref name="getMethod"/> (sync, stateless) that is used if the value is not yet available
        /// </summary>
        public static async ValueTask<T> GetAsync<T>(this IDistributedCache cache, string key, Func<T> getMethod,
            DistributedCacheEntryOptions? options = null, CancellationToken cancellation = default)
            => await GetAsyncShared<int, T>(cache, key, state: 0, getMethod, options, cancellation).ConfigureAwait(false); // use dummy state

        /// <summary>
        /// Gets a value from cache, with a caller-supplied <paramref name="getMethod"/> (async, stateful) that is used if the value is not yet available
        /// </summary>
        public static async ValueTask<T> GetAsync<TState, T>(this IDistributedCache cache, string key, TState state, Func<TState, CancellationToken, ValueTask<T>> getMethod,
            DistributedCacheEntryOptions? options = null, CancellationToken cancellation = default)
            => await GetAsyncShared<TState, T>(cache, key, state, getMethod, options, cancellation).ConfigureAwait(false);

        /// <summary>
        /// Gets a value from cache, with a caller-supplied <paramref name="getMethod"/> (sync, stateful) that is used if the value is not yet available
        /// </summary>
        public static async ValueTask<T> GetAsync<TState, T>(this IDistributedCache cache, string key, TState state, Func<TState, T> getMethod,
            DistributedCacheEntryOptions? options = null, CancellationToken cancellation = default)
            => await GetAsyncShared<TState, T>(cache, key, state, getMethod, options, cancellation).ConfigureAwait(false);

        /// <summary>
        /// Provides a common implementation for the public-facing API, to avoid duplication
        /// </summary>
        private static async ValueTask<T> GetAsyncShared<TState, T>(IDistributedCache cache, string key, TState state, Delegate getMethod,
            DistributedCacheEntryOptions? options, CancellationToken cancellation)
        {
            var pending = cache.GetAsync(key, cancellation);
            if (!pending.IsCompletedSuccessfully)
            {
                // async-result was not available immediately; go full-async
                return await Awaited(cache, key, pending, state, getMethod, options, cancellation).ConfigureAwait(false);
            }

            var bytes = pending.Result;
            if (bytes is null)
            {
                // data is missing; go async for everything else
                return await Awaited(cache, key, null, state, getMethod, options, cancellation).ConfigureAwait(false);
            }

            // data was available synchronously; deserialize
            return Deserialize<T>(bytes);

            static async Task<T> Awaited(
                IDistributedCache cache, // the underlying cache
                string key, // the key on the cache
                Task<byte[]?>? pending, // incomplete "get bytes" operation, if any
                TState state, // state possibly used by the get-method
                Delegate getMethod, // the get-method supplied by the caller
                DistributedCacheEntryOptions? options, // cache expiration, etc
                CancellationToken cancellation)
            {
                byte[]? bytes;
                if (pending is not null)
                {
                    bytes = await pending.ConfigureAwait(false);
                    if (bytes is not null)
                    {   // data was available asynchronously
                        return Deserialize<T>(bytes);
                    }
                }

                // Acquire the semaphore for the specific cache key
                var myLock = _keyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
                await myLock.WaitAsync(cancellation).ConfigureAwait(false);
                try
                {
                    // Double-check if the data was added to the cache while waiting for the lock
                    bytes = await cache.GetAsync(key, cancellation).ConfigureAwait(false);
                    if (bytes != null)
                    {
                        return Deserialize<T>(bytes);
                    }

                    // Compute the value since it's still not in the cache
                    var result = getMethod switch
                    {
                        Func<TState, CancellationToken, ValueTask<T>> get => await get(state, cancellation).ConfigureAwait(false),
                        Func<TState, T> get => get(state),
                        Func<CancellationToken, ValueTask<T>> get => await get(cancellation).ConfigureAwait(false),
                        Func<T> get => get(),
                        _ => throw new ArgumentException(nameof(getMethod)),
                    };
                    bytes = Serialize(result);
                    if (options is null)
                    {
                        await cache.SetAsync(key, bytes, cancellation).ConfigureAwait(false);
                    }
                    else
                    {
                        await cache.SetAsync(key, bytes, options, cancellation).ConfigureAwait(false);
                    }
                    return result;
                }
                finally
                {
                    myLock.Release();

                    // Clean up the semaphore if no other threads are waiting
                    if (myLock.CurrentCount == 1)
                    {
                        _keyLocks.TryRemove(key, out _);
                    }
                }
            }
        }

        // The current cache API is byte[]-based, but a wide range of
        // serializer choices are possible; here we use the inbuilt
        // System.Text.Json.JsonSerializer, which is a fair compromise
        // between being easy to configure and use on general types,
        // versus raw performance. Alternative (non-byte[]) storage
        // mechanisms are under consideration.
        //
        // If it is likely that you will change serializers during
        // upgrades (and you are using out-of-process storage), then
        // you may wish to use a sentinel prefix before the payload,
        // to allow you to safely switch between serializers;
        // alternatively, you may choose to use a key-prefix so that
        // the old data is simply not found (and expires naturally)
        private static T Deserialize<T>(byte[] bytes)
        {
            return JsonSerializer.Deserialize<T>(bytes)!;
        }

        private static byte[] Serialize<T>(T value)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(buffer);
            JsonSerializer.Serialize(writer, value);
            return buffer.WrittenSpan.ToArray();
        }
    }
}
