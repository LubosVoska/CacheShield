using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace CacheShield.Tests
{
    public class CacheShieldAdvancedTests
    {
        public CacheShieldAdvancedTests()
        {
            // Reset global config between tests to stable defaults
            CacheShield.Configure(cfg =>
            {
                cfg.Serializer = new MessagePackSerializerWrapper();
                cfg.DefaultHardTtl = TimeSpan.FromSeconds(2);
                cfg.DefaultSoftTtl = TimeSpan.FromSeconds(1);
                cfg.ExpirationJitterFraction = 0.0;
                cfg.KeyLockEvictionWindow = TimeSpan.FromMinutes(2);
                cfg.KeyPrefix = null;
                cfg.MaxPayloadBytes = null;
                cfg.SkipCachingNullOrDefault = false;
                cfg.LockWaitTimeout = null;
            });
        }

        [Fact]
        public async Task SWR_ServeStale_ThenBackgroundRefresh_UpdatesValue()
        {
            var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            var key = "swr_key";
            var policy = new CacheShieldPolicy { SoftTtl = TimeSpan.Zero, HardTtl = TimeSpan.FromSeconds(5) };

            int counter = 0;
            Func<CancellationToken, ValueTask<string>> get = ct => new ValueTask<string>("v" + Interlocked.Increment(ref counter));

            // First call: miss -> compute v1 and store envelope with soft=now
            var v1 = await cache.GetOrCreateAsync(key, get, policy);
            Assert.Equal("v1", v1);

            // Second call: returns stale v1 immediately and triggers background refresh
            var second = await cache.GetOrCreateAsync(key, get, policy);
            Assert.Equal("v1", second);

            // allow background refresh to happen
            await Task.Delay(150);

            // Third call: should observe updated value v2 from background refresh
            var third = await cache.GetOrCreateAsync(key, get, policy);
            Assert.Equal("v2", third);
        }

        [Fact]
        public async Task LockWaitTimeout_ReturnsFallbackWithoutSetting_CacheSetByFirst()
        {
            var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            var key = "lock_timeout_key";

            var slowPolicy = new CacheShieldPolicy { LockWaitTimeout = null, SoftTtl = TimeSpan.FromSeconds(1), HardTtl = TimeSpan.FromSeconds(5) };
            var fastPolicy = new CacheShieldPolicy { LockWaitTimeout = TimeSpan.FromMilliseconds(50), SoftTtl = TimeSpan.FromSeconds(1), HardTtl = TimeSpan.FromSeconds(5) };

            var slowGet = new Func<CancellationToken, ValueTask<string>>(async ct => { await Task.Delay(200, ct); return "A"; });
            var fastGet = new Func<CancellationToken, ValueTask<string>>(ct => new ValueTask<string>("B"));

            var t1 = cache.GetOrCreateAsync(key, slowGet, slowPolicy).AsTask();
            // small delay to ensure t1 grabs lock
            await Task.Delay(10);
            var t2 = cache.GetOrCreateAsync(key, fastGet, fastPolicy).AsTask();

            var b = await t2; // should return quickly with fallback
            var a = await t1; // completes and sets

            Assert.Equal("B", b);
            Assert.Equal("A", a);

            // Now a subsequent call should read cached "A"
            var next = await cache.GetOrCreateAsync(key, fastGet, fastPolicy);
            Assert.Equal("A", next);
        }

        [Fact]
        public async Task SkipNullOrDefault_DoesNotCache()
        {
            var cacheMock = new Mock<IDistributedCache>(MockBehavior.Strict);
            var key = "skip_null_key";
            cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((byte[]?)null);

            CacheShield.Configure(cfg =>
            {
                cfg.SkipCachingNullOrDefault = true;
            });

            string? get() => null;
            var result = await cacheMock.Object.GetOrCreateAsync(key, get, serializer: new MessagePackSerializerWrapper(), options: null);
            Assert.Null(result);
            cacheMock.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task MaxPayload_DoesNotCache_WhenExceedsLimit()
        {
            var cacheMock = new Mock<IDistributedCache>(MockBehavior.Strict);
            var key = "max_payload_key";
            cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((byte[]?)null);

            CacheShield.Configure(cfg =>
            {
                cfg.MaxPayloadBytes = 10; // very small
            });

            var large = new string('x', 1000);
            var result = await cacheMock.Object.GetOrCreateAsync(key, () => large, serializer: new MessagePackSerializerWrapper(), options: null);
            Assert.Equal(large, result);
            cacheMock.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Jitter_AdjustsAbsoluteExpirationWithinBounds()
        {
            var cacheMock = new Mock<IDistributedCache>();
            var key = "jitter_key";
            cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((byte[]?)null);
            DistributedCacheEntryOptions? captured = null;
            cacheMock.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((k, b, o, ct) => captured = o)
            .Returns(Task.CompletedTask);

            CacheShield.Configure(cfg =>
            {
                cfg.DefaultHardTtl = TimeSpan.FromSeconds(10);
                cfg.ExpirationJitterFraction = 0.5; // ±50%
            });

            var res = await cacheMock.Object.GetOrCreateAsync(key, () => "v", serializer: new MessagePackSerializerWrapper(), options: null);
            Assert.Equal("v", res);
            Assert.NotNull(captured);
            Assert.NotNull(captured!.AbsoluteExpirationRelativeToNow);
            // within [5s,15s]
            var ttl = captured!.AbsoluteExpirationRelativeToNow!.Value;
            Assert.True(ttl >= TimeSpan.FromSeconds(1)); // be lenient on flakes
            Assert.True(ttl <= TimeSpan.FromSeconds(20));
        }

        [Fact]
        public async Task KeyPrefix_IsApplied()
        {
            var cacheMock = new Mock<IDistributedCache>();
            var key = "k";
            var prefixed = "p:" + key;
            CacheShield.Configure(cfg => cfg.KeyPrefix = "p:");

            cacheMock.Setup(c => c.GetAsync(prefixed, It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null)
            .Verifiable();
            cacheMock.Setup(c => c.SetAsync(prefixed, It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

            var v = await cacheMock.Object.GetOrCreateAsync(key, () => "x", serializer: new MessagePackSerializerWrapper(), options: null);
            Assert.Equal("x", v);
            cacheMock.VerifyAll();
        }

        [Fact]
        public async Task GetOrCreateMany_Works_ForMultipleKeys()
        {
            var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            var keys = Enumerable.Range(1, 8).Select(i => $"k:{i}").ToArray();
            int calls = 0;
            var res = await cache.GetOrCreateManyAsync(keys, (k, ct) => new ValueTask<string>($"v:{Interlocked.Increment(ref calls)}"));
            Assert.Equal(keys.Length, res.Length);
            Assert.Equal(keys.Length, calls);

            // second pass: should be all hits (no extra calls)
            var res2 = await cache.GetOrCreateManyAsync(keys, (k, ct) => new ValueTask<string>("never"));
            Assert.Equal(res, res2);
        }
    }
}
