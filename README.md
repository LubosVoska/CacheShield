# CacheShield

CacheShield is a .NET library that extends `IDistributedCache` to prevent cache stampede issues by using per-key asynchronous locks. It ensures that only one caller computes the value when it's missing or expired, improving performance and reducing load on your data source.

Inspired by: https://github.com/mgravell/DistributedCacheDemo

## Features

- Prevent Cache Stampede: Ensures only one caller computes a missing cache value.
- Stale-While-Revalidate (SWR): Serve stale data after soft TTL while a single background refresh updates the entry.
- Early Refresh + Jitter: Proactively refresh near expiry and apply randomized expiration jitter to avoid thundering herds.
- Lock Wait Timeout + Fallback: Avoid head-of-line blocking by returning stale/best-effort when lock wait exceeds a threshold.
- Asynchronous Support: Fully supports async patterns (`ValueTask`-based get delegates).
- Bulk API: `GetOrCreateManyAsync` for batched keys with bounded concurrency.
- Easy Integration: Works with any `IDistributedCache` implementation.
- Configurable Cache Options: Includes a wide range of predefined cache durations for convenience.
- Stateful and Stateless Methods: Supports both stateful and stateless getMethod delegates.
- Custom Serialization: Allows customization of serialization mechanisms if needed.
- MessagePack Serialization: Uses MessagePack for efficient binary serialization by default.
- Memory-bounded keyed locks: Uses a per-key lock pool with ref-counting and sliding eviction to prevent unbounded lock growth.
- Global configuration: `CacheShield.Configure` for defaults like serializer, TTLs, jitter, key prefix, payload limits, and lock eviction.
- Diagnostics: Emits OpenTelemetry-compatible metrics and activities (ActivitySource `CacheShield`, Meter `CacheShield`).

## Installation

Install via NuGet: [Nuget package](https://www.nuget.org/packages/CacheShield)
```bash
dotnet add package CacheShield
```

Or via the NuGet Package Manager:

```powershell
Install-Package CacheShield
```

## Usage
### Basic Usage
To use CacheShield, call the `GetOrCreateAsync` extension method on your `IDistributedCache` instance. Provide a cache key, a method to retrieve the value if it's not in the cache, and optionally, cache entry options.

```csharp
using Microsoft.Extensions.Caching.Distributed;
using CacheShield;

public class MyService
{
 private readonly IDistributedCache _cache;

 public MyService(IDistributedCache cache)
 {
 _cache = cache;
 }

 public async Task<MyData> GetDataAsync(string id)
 {
 var data = await _cache.GetOrCreateAsync($"data:{id}", async cancellationToken =>
 {
 // This code runs only if the data is not in the cache
 return await FetchDataFromDatabaseAsync(id, cancellationToken);
 }, options: CacheOptions.FiveMinutes);

 return data;
 }

 private Task<MyData> FetchDataFromDatabaseAsync(string id, CancellationToken cancellationToken)
 {
 // Implementation to fetch data from the database
 }
}
```

### Stale?While?Revalidate and Early Refresh
Use a `CacheShieldPolicy` to enable SWR and early refresh with jittered expirations.

```csharp
var policy = new CacheShieldPolicy
{
 SoftTtl = TimeSpan.FromMinutes(2),
 HardTtl = TimeSpan.FromMinutes(10),
 EarlyRefreshWindow = TimeSpan.FromSeconds(30),
 ExpirationJitterFraction =0.15
};

var value = await _cache.GetOrCreateAsync(
 "product:123",
 ct => FetchFromDbAsync(123, ct),
 policy);
```

Behavior:
- If soft TTL not reached: return cached value.
- If soft TTL passed but before hard TTL: serve stale and refresh in background.
- If beyond hard TTL: block a single refresher under per-key lock.

### Global Configuration
Set global defaults at startup.

```csharp
CacheShield.Configure(cfg =>
{
 cfg.Serializer = new MessagePackSerializerWrapper();
 cfg.DefaultSoftTtl = TimeSpan.FromMinutes(2);
 cfg.DefaultHardTtl = TimeSpan.FromMinutes(10);
 cfg.ExpirationJitterFraction =0.1;
 cfg.KeyPrefix = "prod:"; // optional
 cfg.MaxPayloadBytes =1_000_000; // optional safeguard
 cfg.SkipCachingNullOrDefault = true; // optional
 cfg.KeyLockEvictionWindow = TimeSpan.FromMinutes(2);
 cfg.LockWaitTimeout = TimeSpan.FromSeconds(5); // default lock wait, can be overridden per call
});
```

### Lock Wait Timeout and Fallback
Per-call override with policy:

```csharp
var policy = new CacheShieldPolicy
{
 SoftTtl = TimeSpan.FromMinutes(2),
 HardTtl = TimeSpan.FromMinutes(10),
 LockWaitTimeout = TimeSpan.FromMilliseconds(100)
};

var value = await _cache.GetOrCreateAsync("key", ct => ComputeAsync(ct), policy);
```

If lock can’t be acquired within timeout:
- Returns last cached payload (even stale) if present.
- Otherwise computes without setting.

### Bulk Get
```csharp
var keys = new[]{"k:1","k:2","k:3"};
var results = await _cache.GetOrCreateManyAsync(keys, (k, ct) => LoadAsync(k, ct), maxConcurrency:8);
```

### Using Predefined Cache Durations
CacheShield includes a `CacheOptions` class with predefined cache durations.

```csharp
var cacheEntryOptions = CacheOptions.ThirtyMinutes;

var value = await _cache.GetOrCreateAsync("cacheKey", async cancellationToken =>
{
 return await ComputeValueAsync(cancellationToken);
}, options: cacheEntryOptions);
```

### Stateless, Stateful, and Sync Overloads
All existing overloads remain and work. Policy-enabled overloads are added; choose the simpler ones if you don’t need SWR.

### Diagnostics
- ActivitySource: `CacheShield`
- Meter: `CacheShield`
Metrics: hits, misses, stale-served, refresh-started/completed, deserialize-failures, lock-wait-ms, compute-ms.

Hook into OpenTelemetry to export.

```csharp
// Program.cs (.NET8/9)
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
 .ConfigureResource(rb => rb.AddService("my-service"))
 .WithMetrics(mb => mb
 .AddMeter("CacheShield")
 .AddAspNetCoreInstrumentation()
 .AddRuntimeInstrumentation()
 .AddProcessInstrumentation()
 .AddPrometheusExporter())
 .WithTracing(tb => tb
 .AddSource("CacheShield")
 .AddAspNetCoreInstrumentation()
 .AddHttpClientInstrumentation()
 .AddConsoleExporter());
```

Note: On `netstandard2.1` targets, diagnostics are compiled as no-ops.

## Advanced samples

###1) Dynamic SWR policy per key
```csharp
CacheShieldPolicy SelectPolicyFor(string key) => key.StartsWith("user:")
 ? new CacheShieldPolicy { SoftTtl = TimeSpan.FromMinutes(1), HardTtl = TimeSpan.FromMinutes(5), EarlyRefreshWindow = TimeSpan.FromSeconds(15) }
 : new CacheShieldPolicy { SoftTtl = TimeSpan.FromMinutes(5), HardTtl = TimeSpan.FromMinutes(20), EarlyRefreshWindow = TimeSpan.FromMinutes(1), ExpirationJitterFraction =0.2 };

var policy = SelectPolicyFor(cacheKey);
var value = await _cache.GetOrCreateAsync(cacheKey, ct => GetDataAsync(ct), policy);
```

###2) Two-level cache pattern (IMemoryCache L1 over `IDistributedCache` L2)
```csharp
public class TwoLevelCache
{
 private readonly IMemoryCache _l1;
 private readonly IDistributedCache _l2;

 public TwoLevelCache(IMemoryCache l1, IDistributedCache l2)
 { _l1 = l1; _l2 = l2; }

 public async Task<T> GetAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, CacheShieldPolicy? policy = null, TimeSpan? l1Ttl = null, CancellationToken ct = default)
 {
 if (_l1.TryGetValue(key, out T value)) return value!;
 var v = policy is null
 ? await _l2.GetOrCreateAsync(key, factory, serializer: null, options: null, cancellationToken: ct)
 : await _l2.GetOrCreateAsync(key, factory, policy, serializer: null, options: null, cancellationToken: ct);
 _l1.Set(key, v, l1Ttl ?? TimeSpan.FromSeconds(30)); // L1 TTL <= L2 soft TTL is typical
 return v;
 }
}
```

###3) Key prefixing and versioning
```csharp
// Startup default: prefix all keys for environment/tenant segregation
CacheShield.Configure(cfg => cfg.KeyPrefix = "prod:tenantA:");

// Version bump: rotate data group by changing prefix
CacheShield.Configure(cfg => cfg.KeyPrefix = "prod:tenantA:v2:");
```

###4) Skip null/defaults and cap payload size
```csharp
CacheShield.Configure(cfg =>
{
 cfg.SkipCachingNullOrDefault = true; // avoid caching missing/sentinel results
 cfg.MaxPayloadBytes =512 *1024; //512 KB cap
});
```

###5) Custom serializer with `System.Text.Json`
```csharp
public sealed class SystemTextJsonSerializer : ISerializer
{
 private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
 {
 DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
 WriteIndented = false,
 PropertyNameCaseInsensitive = true
 };

 public byte[] Serialize<T>(T value)
 {
 return JsonSerializer.SerializeToUtf8Bytes(value, _options);
 }

 public T Deserialize<T>(byte[] bytes)
 {
 return JsonSerializer.Deserialize<T>(bytes, _options)!;
 }
}

// Register as default
CacheShield.Configure(cfg => cfg.Serializer = new SystemTextJsonSerializer());
```

###6) Lock wait timeouts under load
```csharp
var hotKey = "hot:feed";
var slowPolicy = new CacheShieldPolicy
{
 SoftTtl = TimeSpan.FromSeconds(5),
 HardTtl = TimeSpan.FromSeconds(30),
 LockWaitTimeout = TimeSpan.FromMilliseconds(50)
};

var v = await _cache.GetOrCreateAsync(hotKey, async ct =>
{
 await Task.Delay(500, ct); // simulate slow origin
 return await ComputeHotFeedAsync(ct);
}, slowPolicy);
```

###7) Bulk pre-warm and fetch
```csharp
var ids = Enumerable.Range(1,200).Select(i => $"product:{i}").ToArray();
var products = await _cache.GetOrCreateManyAsync(ids, (k, ct) => LoadProductAsync(k, ct), maxConcurrency:16);
```

## Under the hood: keyed-lock pool with sliding eviction

- Per-key locking is implemented using a pool of `SemaphoreSlim` instances keyed by cache key.
- Each lock keeps a ref-count and last-used timestamp.
- Locks are:
 - Rented when a request arrives for a key (ref-count++ and last-used updated).
 - Released when the request completes (ref-count--).
- A background sweeper periodically evicts idle locks (ref-count ==0) that have not been used for a sliding window (default from `CacheShield.Config`).

Notes:
- The keyed locks are process-local. For cross-instance stampede protection, apply a distributed lock in your compute path.
- Corrupted cache payloads (deserialization failures) are automatically removed and recomputed.
- `CacheOptions.Infinite` represents “no expiration”. Use with care.

## Contributing
Contributions are welcome! Please open an issue or submit a pull request on GitHub.

### Reporting Issues
If you encounter any bugs or have suggestions, please create an issue on the GitHub repository.

### Pull Requests
When submitting a pull request:

* Ensure your code follows the project's coding standards.
* Include unit tests for any new functionality.
* Update documentation as necessary.

## License
This project is licensed under the MIT License - see the LICENSE file for details.

#### Thank you for using CacheShield! If you find this library helpful, please consider giving it a star on GitHub.

