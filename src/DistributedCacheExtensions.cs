using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace CacheShield
{
    /// <summary>
    /// Provides extension methods for <see cref="IDistributedCache"/> to prevent cache stampede issues.
    /// </summary>
    public static class DistributedCacheExtensions
    {
        // Dictionary to hold per-key semaphores with cleanup
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new();

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
                catch (Exception ex)
                {
                    // Optionally log the deserialization error
                    // Remove the corrupted cache entry
                    await cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
                    // Depending on requirements, you might choose to throw or continue to fetch fresh data
                }
            }

            // Acquire the semaphore for the specific cache key
            var semaphore = _keyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                    catch (Exception ex)
                    {
                        // Optionally log the deserialization error
                        await cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
                        // Depending on requirements, you might choose to throw or continue to fetch fresh data
                    }
                }

                // Data is still not in cache; invoke the getMethod to retrieve it
                T result = await getMethod(cancellationToken).ConfigureAwait(false);

                // Serialize the result
                byte[] serializedData = serializer.Serialize(result);

                // Set the cache entry with provided options or default options
                var cacheOptions = options ?? new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) // Default expiration
                };

                await cache.SetAsync(key, serializedData, cacheOptions, cancellationToken).ConfigureAwait(false);

                return result;
            }
            catch (Exception)
            {
                // Optionally log the exception
                throw;
            }
            finally
            {
                semaphore.Release();

                // Cleanup: Remove the semaphore if no other threads are waiting
                if (semaphore.CurrentCount == 1)
                {
                    _keyLocks.TryRemove(key, out _);
                    semaphore.Dispose();
                }
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

            // Wrap the synchronous getMethod in an asynchronous lambda
            return await cache.GetOrCreateAsync(key, ct => new ValueTask<T>(getMethod()), serializer, options, cancellationToken).ConfigureAwait(false);
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

            // Wrap the stateful getMethod in a state-less lambda
            return await cache.GetOrCreateAsync(key, ct => getMethod(state, ct), serializer, options, cancellationToken).ConfigureAwait(false);
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

            // Wrap the stateful getMethod in an asynchronous lambda
            return await cache.GetOrCreateAsync(key, ct => new ValueTask<T>(getMethod(state)), serializer, options, cancellationToken).ConfigureAwait(false);
        }
    }
}
