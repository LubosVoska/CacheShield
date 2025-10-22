using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CacheShield
{
    /// <summary>
    /// Provides extension methods for <see cref="IDistributedCache"/> to prevent cache stampede issues.
    /// </summary>
    public static class DistributedCacheExtensions
    {
        // Keyed lock pool with ref-counting and sliding eviction
        private static readonly KeyLockPool _lockPool = KeyLockPool.Shared;

        // Default serializer instance
        private static readonly ISerializer _defaultSerializer = new MessagePackSerializerWrapper();

        /// <summary>
        /// Gets a value from cache, with a caller-supplied asynchronous <paramref name="getMethod"/> (stateless) used if the value is not available.
        /// If no serializer is provided, the default <see cref="MessagePackSerializerWrapper"/> is used.
        /// </summary>
        public static async ValueTask<T> GetOrCreateAsync<T>(
            this IDistributedCache cache,
            string key,
            Func<CancellationToken, ValueTask<T>> getMethod,
            ISerializer? serializer = null,
            DistributedCacheEntryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (cache is null) throw new ArgumentNullException(nameof(cache));
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));
            if (getMethod is null) throw new ArgumentNullException(nameof(getMethod));

            serializer ??= _defaultSerializer;

            // Attempt to get the cached data
            byte[]? cachedData = await cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
            if (cachedData != null)
            {
                try
                {
                    return serializer.Deserialize<T>(cachedData);
                }
                catch
                {
                    // Remove the corrupted cache entry and continue to fetch fresh data
                    await cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
                }
            }

            // Acquire per-key lock via pool
            var entry = _lockPool.Rent(key);
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Double-check if the data was added to the cache while waiting for the lock
                cachedData = await cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
                if (cachedData != null)
                {
                    try
                    {
                        return serializer.Deserialize<T>(cachedData);
                    }
                    catch
                    {
                        await cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
                    }
                }

                // Data is still not in cache; invoke the getMethod to retrieve it
                T result = await getMethod(cancellationToken).ConfigureAwait(false);

                // Serialize the result
                byte[] serializedData = serializer.Serialize(result);

                // Use provided options or a safe default
                var cacheOptions = options != null
                    ? Clone(options)
                    : new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                    };

                await cache.SetAsync(key, serializedData, cacheOptions, cancellationToken).ConfigureAwait(false);

                return result;
            }
            finally
            {
                entry.Semaphore.Release();
                _lockPool.Return(key, entry);
            }
        }

        /// <summary>
        /// Gets a value from cache, with a caller-supplied synchronous <paramref name="getMethod"/> (stateless) used if the value is not available.
        /// If no serializer is provided, the default <see cref="MessagePackSerializerWrapper"/> is used.
        /// </summary>
        public static async ValueTask<T> GetOrCreateAsync<T>(
            this IDistributedCache cache,
            string key,
            Func<T> getMethod,
            ISerializer? serializer = null,
            DistributedCacheEntryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (getMethod is null) throw new ArgumentNullException(nameof(getMethod));

            return await cache.GetOrCreateAsync(
                key,
                ct => new ValueTask<T>(getMethod()),
                serializer,
                options,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets a value from cache, with a caller-supplied asynchronous <paramref name="getMethod"/> (stateful) used if the value is not available.
        /// If no serializer is provided, the default <see cref="MessagePackSerializerWrapper"/> is used.
        /// </summary>
        public static async ValueTask<T> GetOrCreateAsync<TState, T>(
            this IDistributedCache cache,
            string key,
            TState state,
            Func<TState, CancellationToken, ValueTask<T>> getMethod,
            ISerializer? serializer = null,
            DistributedCacheEntryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (getMethod is null) throw new ArgumentNullException(nameof(getMethod));

            return await cache.GetOrCreateAsync(
                key,
                ct => getMethod(state, ct),
                serializer,
                options,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets a value from cache, with a caller-supplied synchronous <paramref name="getMethod"/> (stateful) used if the value is not available.
        /// If no serializer is provided, the default <see cref="MessagePackSerializerWrapper"/> is used.
        /// </summary>
        public static async ValueTask<T> GetOrCreateAsync<TState, T>(
            this IDistributedCache cache,
            string key,
            TState state,
            Func<TState, T> getMethod,
            ISerializer? serializer = null,
            DistributedCacheEntryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (getMethod is null) throw new ArgumentNullException(nameof(getMethod));

            return await cache.GetOrCreateAsync(
                key,
                ct => new ValueTask<T>(getMethod(state)),
                serializer,
                options,
                cancellationToken).ConfigureAwait(false);
        }

        private static DistributedCacheEntryOptions Clone(DistributedCacheEntryOptions src)
        {
            return new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = src.AbsoluteExpiration,
                AbsoluteExpirationRelativeToNow = src.AbsoluteExpirationRelativeToNow,
                SlidingExpiration = src.SlidingExpiration
            };
        }
    }
}
