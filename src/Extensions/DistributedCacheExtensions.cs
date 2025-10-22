namespace CacheShield;

/// <summary>
/// Provides extension methods for <see cref="IDistributedCache"/> to prevent cache stampede issues.
/// </summary>
public static class DistributedCacheExtensions
{
    // Keyed lock pool with ref-counting and sliding eviction
    private static readonly KeyLockPool _lockPool = KeyLockPool.Shared;

    // Default serializer instance (kept for back-compat); prefer CacheShield.Config
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

        serializer ??= CacheShield.Config.Serializer ?? _defaultSerializer;
        key = ApplyPrefix(CacheShield.Config.KeyPrefix, key);

        // Attempt to get the cached data
        byte[]? cachedData = await cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (cachedData != null)
        {
            try
            {
                CacheShieldDiagnostics.Hits.Add(1);
                return serializer.Deserialize<T>(cachedData);
            }
            catch
            {
                CacheShieldDiagnostics.DeserializationFailures.Add(1);
                // Remove the corrupted cache entry once and continue to fetch fresh data
                await cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            }
        }

        // Acquire per-key lock via pool
        var entry = _lockPool.Rent(key);
        var swWait = Stopwatch.StartNew();
        await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        CacheShieldDiagnostics.LockWaitMs.Record(swWait.Elapsed.TotalMilliseconds);
        try
        {
            // Double-check if the data was added to the cache while waiting for the lock
            cachedData = await cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
            if (cachedData != null)
            {
                try
                {
                    CacheShieldDiagnostics.Hits.Add(1);
                    return serializer.Deserialize<T>(cachedData);
                }
                catch
                {
                    CacheShieldDiagnostics.DeserializationFailures.Add(1);
                    await cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
                }
            }

            // Data is still not in cache; invoke the getMethod to retrieve it
            var swCompute = Stopwatch.StartNew();
            T result = await getMethod(cancellationToken).ConfigureAwait(false);
            CacheShieldDiagnostics.ComputeMs.Record(swCompute.Elapsed.TotalMilliseconds);

            // Serialize the result
            if (CacheShield.Config.SkipCachingNullOrDefault && IsNullOrDefault(result))
            {
                return result; // do not cache
            }

            byte[] serializedData = serializer.Serialize(result);
            if (CacheShield.Config.MaxPayloadBytes is long max && serializedData.LongLength > max)
            {
                return result; // do not cache oversized payloads
            }

            // Use provided options or a safe default
            var cacheOptions = options != null
            ? Clone(options)
            : new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheShield.Config.DefaultHardTtl
            };

            // optional jitter only when using defaults
            if (options == null)
            {
                ApplyJitter(cacheOptions, CacheShield.Config.ExpirationJitterFraction);
            }

            await cache.SetAsync(key, serializedData, cacheOptions, cancellationToken).ConfigureAwait(false);

            CacheShieldDiagnostics.Misses.Add(1);
            return result;
        }
        finally
        {
            entry.Semaphore.Release();
            _lockPool.Return(key, entry);
        }
    }

    /// <summary>
    /// Policy-enabled overload: supports SWR, early refresh, jitter, lock wait timeout, etc.
    /// </summary>
    public static async ValueTask<T> GetOrCreateAsync<T>(
    this IDistributedCache cache,
    string key,
    Func<CancellationToken, ValueTask<T>> getMethod,
    CacheShieldPolicy policy,
    ISerializer? serializer = null,
    DistributedCacheEntryOptions options = null,
    CancellationToken cancellationToken = default)
    {
        if (cache is null) throw new ArgumentNullException(nameof(cache));
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));
        if (getMethod is null) throw new ArgumentNullException(nameof(getMethod));
        if (policy is null) throw new ArgumentNullException(nameof(policy));

        serializer ??= CacheShield.Config.Serializer ?? _defaultSerializer;
        key = ApplyPrefix(CacheShield.Config.KeyPrefix, key);
        var now = DateTimeOffset.UtcNow;

        // Try read
        byte[]? bytes = await cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (bytes != null)
        {
            // Try interpret as envelope first
            if (TryDeserializeEnvelope(bytes, serializer, out CacheEnvelope<T> env))
            {
                CacheShieldDiagnostics.Hits.Add(1);
                var soft = env.SoftExpireUtc;
                // Early refresh if still fresh but nearing expiry
                if (policy.EarlyRefreshWindow is TimeSpan w && w > TimeSpan.Zero)
                {
                    // estimate hard expire = options?.AbsoluteExpirationRelativeToNow or policy.HardTtl or global default
                    var hardTtl = policy.HardTtl ?? CacheShield.Config.DefaultHardTtl;
                    var createdAt = soft - (policy.SoftTtl ?? CacheShield.Config.DefaultSoftTtl);
                    var hardExpire = createdAt + hardTtl;
                    var remaining = hardExpire - now;
                    if (remaining <= w && remaining > TimeSpan.Zero)
                    {
                        _ = TriggerBackgroundRefresh(cache, key, getMethod, policy, serializer, options);
                    }
                }

                if (now <= soft)
                {
                    return env.Value; // still fresh
                }
                else
                {
                    // stale-while-revalidate if within hard TTL
                    var hardTtl = policy.HardTtl ?? CacheShield.Config.DefaultHardTtl;
                    var createdAt = soft - (policy.SoftTtl ?? CacheShield.Config.DefaultSoftTtl);
                    var hardExpire = createdAt + hardTtl;
                    if (now <= hardExpire)
                    {
                        CacheShieldDiagnostics.StaleServed.Add(1);
                        _ = TriggerBackgroundRefresh(cache, key, getMethod, policy, serializer, options);
                        return env.Value;
                    }
                    // beyond hard expiry: proceed to compute under lock
                }
            }
            else
            {
                // Not an envelope; treat as plain T (back-compat)
                try
                {
                    CacheShieldDiagnostics.Hits.Add(1);
                    return serializer.Deserialize<T>(bytes);
                }
                catch
                {
                    CacheShieldDiagnostics.DeserializationFailures.Add(1);
                    await cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        // Need to compute. Try acquire lock with timeout if specified
        var entry = _lockPool.Rent(key);
        bool acquired;
        var timeout = policy.LockWaitTimeout ?? CacheShield.Config.LockWaitTimeout;
        var swWait = Stopwatch.StartNew();
        if (timeout is TimeSpan t && t > TimeSpan.Zero)
        {
            acquired = await entry.Semaphore.WaitAsync(t, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
        }
        CacheShieldDiagnostics.LockWaitMs.Record(swWait.Elapsed.TotalMilliseconds);

        if (!acquired)
        {
            // Timed out; try return last cached if any (even stale), otherwise compute without setting
            if (bytes != null)
            {
                // try plain T first if not envelope
                if (TryDeserializeEnvelope(bytes, serializer, out CacheEnvelope<T> env2))
                {
                    return env2.Value;
                }
                try { return serializer.Deserialize<T>(bytes); }
                catch { /* fall through and compute */ }
            }

            // Compute without setting
            return await getMethod(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            // Double check
            bytes = await cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
            if (bytes != null)
            {
                if (TryDeserializeEnvelope(bytes, serializer, out CacheEnvelope<T> env))
                {
                    var soft = env.SoftExpireUtc;
                    if (now <= soft)
                    {
                        return env.Value;
                    }
                    else
                    {
                        // stale within hard TTL?
                        var hardTtl = policy.HardTtl ?? CacheShield.Config.DefaultHardTtl;
                        var createdAt = soft - (policy.SoftTtl ?? CacheShield.Config.DefaultSoftTtl);
                        var hardExpire = createdAt + hardTtl;
                        if (now <= hardExpire)
                        {
                            CacheShieldDiagnostics.StaleServed.Add(1);
                            _ = TriggerBackgroundRefresh(cache, key, getMethod, policy, serializer, options);
                            return env.Value;
                        }
                    }
                }
                else
                {
                    try { return serializer.Deserialize<T>(bytes); }
                    catch
                    {
                        CacheShieldDiagnostics.DeserializationFailures.Add(1);
                        await cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            // compute
            var swCompute = Stopwatch.StartNew();
            var result = await getMethod(cancellationToken).ConfigureAwait(false);
            CacheShieldDiagnostics.ComputeMs.Record(swCompute.Elapsed.TotalMilliseconds);

            // skip cache?
            if ((policy.SkipCachingNullOrDefault ?? CacheShield.Config.SkipCachingNullOrDefault) && IsNullOrDefault(result))
            {
                return result;
            }

            // serialize, enforce size
            byte[] payload;
            CacheEnvelope<T> envelope;
            var softTtl = policy.SoftTtl ?? CacheShield.Config.DefaultSoftTtl;
            var hardTtlFinal = policy.HardTtl ?? CacheShield.Config.DefaultHardTtl;
            var softExpire = DateTimeOffset.UtcNow + softTtl;
            envelope = new CacheEnvelope<T>(result, softExpire);
            payload = serializer.Serialize(envelope);

            var maxBytes = policy.MaxPayloadBytes ?? CacheShield.Config.MaxPayloadBytes;
            if (maxBytes is long m && payload.LongLength > m)
            {
                return result;
            }

            var cacheOptions = options != null ? Clone(options) : new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = hardTtlFinal
            };
            // jitter
            var frac = policy.ExpirationJitterFraction ?? CacheShield.Config.ExpirationJitterFraction;
            ApplyJitter(cacheOptions, frac);

            await cache.SetAsync(key, payload, cacheOptions, cancellationToken).ConfigureAwait(false);
            CacheShieldDiagnostics.Misses.Add(1);
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
    /// Policy-enabled overload for synchronous get method.
    /// </summary>
    public static ValueTask<T> GetOrCreateAsync<T>(
    this IDistributedCache cache,
    string key,
    Func<T> getMethod,
    CacheShieldPolicy policy,
    ISerializer? serializer = null,
    DistributedCacheEntryOptions? options = null,
    CancellationToken cancellationToken = default)
    => cache.GetOrCreateAsync(key, ct => new ValueTask<T>(getMethod()), policy, serializer, options, cancellationToken);

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
    /// Policy-enabled overload for stateful async delegate.
    /// </summary>
    public static ValueTask<T> GetOrCreateAsync<TState, T>(
    this IDistributedCache cache,
    string key,
    TState state,
    Func<TState, CancellationToken, ValueTask<T>> getMethod,
    CacheShieldPolicy policy,
    ISerializer? serializer = null,
    DistributedCacheEntryOptions? options = null,
    CancellationToken cancellationToken = default)
    => cache.GetOrCreateAsync(key, ct => getMethod(state, ct), policy, serializer, options, cancellationToken);

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

    /// <summary>
    /// Policy-enabled overload for stateful sync delegate.
    /// </summary>
    public static ValueTask<T> GetOrCreateAsync<TState, T>(
    this IDistributedCache cache,
    string key,
    TState state,
    Func<TState, T> getMethod,
    CacheShieldPolicy policy,
    ISerializer? serializer = null,
    DistributedCacheEntryOptions? options = null,
    CancellationToken cancellationToken = default)
    => cache.GetOrCreateAsync(key, ct => new ValueTask<T>(getMethod(state)), policy, serializer, options, cancellationToken);

    /// <summary>
    /// Bulk get-or-create with bounded concurrency. Each key invokes the provided get delegate if missing.
    /// </summary>
    public static async Task<T[]> GetOrCreateManyAsync<T>(
    this IDistributedCache cache,
    string[] keys,
    Func<string, CancellationToken, ValueTask<T>> getMethod,
    int? maxConcurrency = null,
    ISerializer? serializer = null,
    DistributedCacheEntryOptions? options = null,
    CancellationToken cancellationToken = default)
    {
        if (keys is null) throw new ArgumentNullException(nameof(keys));
        if (getMethod is null) throw new ArgumentNullException(nameof(getMethod));
        if (keys.Length == 0) return Array.Empty<T>();

        int degree = Math.Max(1, Math.Min(keys.Length, maxConcurrency ?? Environment.ProcessorCount));
        var results = new T[keys.Length];
        using var gate = new SemaphoreSlim(degree, degree);
        var tasks = new Task[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            var index = i;
            tasks[index] = Task.Run(async () =>
            {
                try
                {
                    results[index] = await cache.GetOrCreateAsync(
         keys[index],
         ct => getMethod(keys[index], ct),
         serializer,
         options,
         cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            }, cancellationToken);
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private static string ApplyPrefix(string? prefix, string key)
    => string.IsNullOrWhiteSpace(prefix) ? key : prefix + key;

    private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

    private static void ApplyJitter(DistributedCacheEntryOptions options, double? fraction)
    {
        if (fraction is null) return;
        var f = Clamp(fraction.Value, 0, 0.9);
        if (f <= 0) return;
        if (options.AbsoluteExpirationRelativeToNow is TimeSpan rel && rel > TimeSpan.Zero)
        {
            // random in [-f, +f]
            var rnd = new Random();
            var delta = (rnd.NextDouble() * 2 - 1) * f;
            var jittered = rel + TimeSpan.FromMilliseconds(rel.TotalMilliseconds * delta);
            if (jittered < TimeSpan.FromMilliseconds(1)) jittered = TimeSpan.FromMilliseconds(1);
            options.AbsoluteExpirationRelativeToNow = jittered;
        }
    }

    private static bool TryDeserializeEnvelope<T>(byte[] bytes, ISerializer serializer, out CacheEnvelope<T> env)
    {
        try
        {
            env = serializer.Deserialize<CacheEnvelope<T>>(bytes);
            return true;
        }
        catch
        {
            env = default;
            return false;
        }
    }

    private static bool IsNullOrDefault<T>(T value)
    {
        if (value is null) return true;
        var type = typeof(T);
        if (type.IsValueType)
        {
            return value!.Equals(default(T));
        }
        return false;
    }

    private static async Task TriggerBackgroundRefresh<T>(
    IDistributedCache cache,
    string key,
    Func<CancellationToken, ValueTask<T>> getMethod,
    CacheShieldPolicy policy,
    ISerializer serializer,
    DistributedCacheEntryOptions? options)
    {
        // Fire-and-forget; never throw
        _ = Task.Run(async () =>
        {
            var entry = _lockPool.Rent(key);
            bool acquired = false;
            try
            {
                acquired = await entry.Semaphore.WaitAsync(TimeSpan.FromSeconds(0.5)).ConfigureAwait(false);
                if (!acquired) return; // someone else is refreshing

                var result = await getMethod(CancellationToken.None).ConfigureAwait(false);
                if ((policy.SkipCachingNullOrDefault ?? CacheShield.Config.SkipCachingNullOrDefault) && IsNullOrDefault(result))
                {
                    return;
                }
                var softTtl = policy.SoftTtl ?? CacheShield.Config.DefaultSoftTtl;
                var hardTtlFinal = policy.HardTtl ?? CacheShield.Config.DefaultHardTtl;
                var env = new CacheEnvelope<T>(result, DateTimeOffset.UtcNow + softTtl);
                var payload = serializer.Serialize(env);
                var maxBytes = policy.MaxPayloadBytes ?? CacheShield.Config.MaxPayloadBytes;
                if (maxBytes is long m && payload.LongLength > m)
                {
                    return;
                }
                var cacheOptions = options != null ? Clone(options) : new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = hardTtlFinal
                };
                // jitter only when using defaults
                if (options == null)
                {
                    var frac = policy.ExpirationJitterFraction ?? CacheShield.Config.ExpirationJitterFraction;
                    ApplyJitter(cacheOptions, frac);
                }

                CacheShieldDiagnostics.RefreshStarted.Add(1);
                await cache.SetAsync(key, payload, cacheOptions, CancellationToken.None).ConfigureAwait(false);
                CacheShieldDiagnostics.RefreshCompleted.Add(1);
            }
            catch
            {
                // swallow
            }
            finally
            {
                if (acquired)
                {
                    entry.Semaphore.Release();
                }
                _lockPool.Return(key, entry);
            }
        });
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
