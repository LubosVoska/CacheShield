# CacheShield

CacheShield is a .NET library that extends `IDistributedCache` to prevent cache stampede issues by using per-key asynchronous locks. It ensures that only one caller computes the value when it's missing or expired, improving performance and reducing load on your data source.

Inspired by: [mgravell/DistributedCacheDemo](https://github.com/mgravell/DistributedCacheDemo)

## Features

* **Prevent Cache Stampede**: Ensures only one caller computes a missing cache value.
* **Asynchronous Support**: Fully supports asynchronous programming patterns.
* **Easy Integration**: Works with any IDistributedCache implementation.
* **Configurable Cache Options**: Includes a wide range of predefined cache durations for convenience.
* **Stateful and Stateless Methods**: Supports both stateful and stateless getMethod delegates.
* **Custom Serialization**: Allows customization of serialization mechanisms if needed.
* **MessagePack Serialization**: Uses MessagePack for efficient binary serialization by default.

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
To use CacheShield, call the GetOrCreateAsync extension method on your `IDistributedCache` instance. Provide a cache key, a method to retrieve the value if it's not in the cache, and optionally, cache entry options.

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

### Using Predefined Cache Durations
CacheShield includes a `CacheOptions` class with over 50 predefined cache durations for convenience.

```csharp
using CacheShield;

// Use a predefined cache duration
var cacheEntryOptions = CacheOptions.ThirtyMinutes;

var value = await _cache.GetOrCreateAsync("cacheKey", async cancellationToken =>
{
    // Compute the value
    return await ComputeValueAsync(cancellationToken);
}, options: cacheEntryOptions);
```

### Stateless Get Method
If your `getMethod` does not need any state or cancellation token, you can use the simpler overload:

```csharp
var value = await _cache.GetOrCreateAsync("simpleKey", () =>
{
    // Compute the value
    return ComputeValue();
});
```

### Stateful Get Method
If your `getMethod` requires state, you can use the stateful overload:

```csharp
var someState = new MyStateObject();

var value = await _cache.GetOrCreateAsync("statefulKey", someState, (state, cancellationToken) =>
{
    // Use the state object
    return ComputeValueWithStateAsync(state, cancellationToken);
});
```

### Preventing Cache Stampede
CacheShield automatically prevents cache stampedes by ensuring that only one caller computes the value when it's missing. Here's how you can simulate multiple concurrent requests:

```csharp
var tasks = new List<Task<string>>();

for (int i = 0; i < 10; i++)
{
    tasks.Add(_cache.GetOrCreateAsync("concurrentKey", async cancellationToken =>
    {
        // Simulate a delay in computing the value
        await Task.Delay(1000, cancellationToken);
        return "Computed Value";
    }));
}

var results = await Task.WhenAll(tasks);

// All tasks will receive the same computed value
```

### Custom Cache Entry Options
You can provide custom cache entry options if the predefined ones do not meet your needs:

```csharp
var customOptions = new DistributedCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15),
    SlidingExpiration = TimeSpan.FromMinutes(5)
};

var value = await _cache.GetOrCreateAsync("customKey", async cancellationToken =>
{
    // Compute the value
    return await GetValueAsync(cancellationToken);
}, options: customOptions);
```

### Synchronous Get Method
If your `getMethod` is synchronous, you can use the overload that accepts a synchronous function:

```csharp
var value = await _cache.GetOrCreateAsync("syncKey", () =>
{
    // Compute the value synchronously
    return "Synchronous Value";
});
```

### Using CancellationToken
You can pass a `CancellationToken` to cancel the operation if needed:

```csharp
var cancellationTokenSource = new CancellationTokenSource();

var value = await _cache.GetOrCreateAsync("cancellableKey", async cancellationToken =>
{
    // Long-running operation that supports cancellation
    return await LongRunningOperationAsync(cancellationToken);
}, cancellationToken: cancellationTokenSource.Token);
```

### Handling Exceptions
If the `getMethod` throws an exception, it will propagate to the caller. You can handle exceptions as needed:

```csharp
try
{
    var value = await _cache.GetOrCreateAsync("exceptionKey", () =>
    {
        // This might throw an exception
        throw new InvalidOperationException("Something went wrong");
    });
}
catch (InvalidOperationException ex)
{
    // Handle the exception
}
```

### Custom Serialization
By default, CacheShield uses MessagePack for serialization. If you need custom serialization, you can implement the `ISerializer` interface and pass your serializer to the `GetOrCreateAsync` method.

```csharp
using CacheShield;

public class NewtonsoftJsonSerializer : ISerializer
{
    public byte[] Serialize<T>(T value)
    {
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(value);
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    public T Deserialize<T>(byte[] bytes)
    {
        var json = System.Text.Encoding.UTF8.GetString(bytes);
        return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
    }
}

// Usage
var customSerializer = new NewtonsoftJsonSerializer();

var value = await _cache.GetOrCreateAsync("customSerializerKey", async cancellationToken =>
{
    // Compute the value
    return await GetValueAsync(cancellationToken);
}, serializer: customSerializer);
```

### Full Example with Custom Cache Options
Here's a complete example that demonstrates various features of CacheShield:

```csharp
using Microsoft.Extensions.Caching.Distributed;
using CacheShield;

public class ProductService
{
    private readonly IDistributedCache _cache;

    public ProductService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<Product> GetProductAsync(int productId)
    {
        var cacheKey = $"product:{productId}";

        var product = await _cache.GetOrCreateAsync(cacheKey, async cancellationToken =>
        {
            // Simulate a database call
            var data = await FetchProductFromDatabaseAsync(productId, cancellationToken);
            return data;
        }, options: CacheOptions.TwelveHours);

        return product;
    }

    private async Task<Product> FetchProductFromDatabaseAsync(int productId, CancellationToken cancellationToken)
    {
        // Simulate delay
        await Task.Delay(500, cancellationToken);

        // Return dummy data
        return new Product
        {
            Id = productId,
            Name = "Sample Product",
            Price = 19.99m
        };
    }
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
}
```


### Advanced Usage with Sliding Expiration
If you want the cache entry to expire if it's not accessed within a certain period, use `SlidingExpiration`:

```csharp
var slidingOptions = new DistributedCacheEntryOptions
{
    SlidingExpiration = TimeSpan.FromMinutes(30)
};

var value = await _cache.GetOrCreateAsync("slidingKey", async cancellationToken =>
{
    // Compute the value
    return await ComputeExpensiveValueAsync(cancellationToken);
}, options: slidingOptions);
```

### Combining Absolute and Sliding Expiration
You can combine both absolute and sliding expiration:

```csharp
var combinedOptions = new DistributedCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6),
    SlidingExpiration = TimeSpan.FromMinutes(30)
};

var value = await _cache.GetOrCreateAsync("combinedKey", async cancellationToken =>
{
    // Compute the value
    return await GetDataAsync(cancellationToken);
}, options: combinedOptions);
```

### Using the Infinite Cache Option
If you have data that rarely changes and you want to cache it indefinitely:

```csharp
var value = await _cache.GetOrCreateAsync("infiniteKey", async cancellationToken =>
{
    // Compute the value
    return await GetStaticDataAsync(cancellationToken);
}, options: CacheOptions.Infinite);
```
Note: Use the `Infinite` option cautiously to avoid consuming excessive memory or stale data.

### Using CacheShield in ASP.NET Core
You can configure `IDistributedCache` in your `Startup.cs` or `Program.cs`:

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddDistributedMemoryCache(); // Or AddStackExchangeRedisCache, etc.
    services.AddScoped<MyService>();
}
```
Then inject `IDistributedCache` into your services and use CacheShield as demonstrated.


### CacheOptions Reference
CacheShield provides the `CacheOptions` class with predefined cache durations:

* Milliseconds:
    * `TenMilliseconds`
    * `FiftyMilliseconds`
    * `OneHundredMilliseconds`
    * `FiveHundredMilliseconds`
* Seconds:
    * `OneSecond`
    * `FiveSeconds`
    * `TenSeconds`
    * `ThirtySeconds`
* Minutes:
    * `OneMinute`
    * `FiveMinutes`
    * `FifteenMinutes`
    * `ThirtyMinutes`
    * `SixtyMinutes`
* Hours:
    * `OneHour`
    * `SixHours`
    * `TwelveHours`
    * `TwentyFourHours`
* Days:
    * `OneDay`
    * `SevenDays`
    * `ThirtyDays`
* Months and Years:
    * `OneMonth` (30 days)
    * `SixMonths` (180 days)
    * `OneYear` (365 days)
* Infinite:
    * `Infinite` (no expiration)

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

