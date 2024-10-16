using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CacheShield.Tests
{
    public class CacheShieldTests
    {
        private readonly Mock<IDistributedCache> _cacheMock;
        private readonly ISerializer _defaultSerializer;

        public CacheShieldTests()
        {
            _cacheMock = new Mock<IDistributedCache>();
            _defaultSerializer = new MessagePackSerializerWrapper();
        }

        [Fact]
        public async Task GetOrCreateAsync_CacheHit_ReturnsCachedValue()
        {
            // Arrange
            string key = "test_key";
            string expectedValue = "cached_value";
            byte[] serializedValue = _defaultSerializer.Serialize(expectedValue);

            _cacheMock.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(serializedValue);

            // Act
            string actualValue = await _cacheMock.Object.GetOrCreateAsync(
                key,
                async ct => await Task.FromResult("new_value"),
                options: null);

            // Assert
            Assert.Equal(expectedValue, actualValue);
            _cacheMock.Verify(c => c.GetAsync(key, It.IsAny<CancellationToken>()), Times.Once);
            _cacheMock.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetOrCreateAsync_CacheMiss_CallsGetMethodAndCachesValue()
        {
            // Arrange
            string key = "test_key";
            string expectedValue = "new_value";
            byte[] serializedValue = _defaultSerializer.Serialize(expectedValue);

            _cacheMock.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
                      .ReturnsAsync((byte[])null);
            _cacheMock.Setup(c => c.SetAsync(key, It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
                      .Returns(Task.CompletedTask)
                      .Verifiable();

            // Act
            string actualValue = await _cacheMock.Object.GetOrCreateAsync(
                key,
                async ct => await Task.FromResult(expectedValue),
                options: new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
                });

            // Assert
            Assert.Equal(expectedValue, actualValue);
            _cacheMock.Verify(c => c.GetAsync(key, It.IsAny<CancellationToken>()), Times.Exactly(2));
            _cacheMock.Verify(c => c.SetAsync(
                key,
                It.Is<byte[]>(b => CompareBytes(b, serializedValue)),
                It.Is<DistributedCacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(10)),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task GetOrCreateAsync_SynchronousGetMethod_CacheHit_ReturnsCachedValue()
        {
            // Arrange
            string key = "test_key";
            int expectedValue = 42;
            byte[] serializedValue = _defaultSerializer.Serialize(expectedValue);

            _cacheMock.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(serializedValue);

            // Act
            int actualValue = await _cacheMock.Object.GetOrCreateAsync(
                key,
                () => expectedValue,
                options: null);

            // Assert
            Assert.Equal(expectedValue, actualValue);
            _cacheMock.Verify(c => c.GetAsync(key, It.IsAny<CancellationToken>()), Times.Once);
            _cacheMock.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetOrCreateAsync_SynchronousGetMethod_CacheMiss_CachesValue()
        {
            // Arrange
            string key = "test_key";
            int expectedValue = 42;
            byte[] serializedValue = _defaultSerializer.Serialize(expectedValue);

            _cacheMock.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
                      .ReturnsAsync((byte[])null);
            _cacheMock.Setup(c => c.SetAsync(key, It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
                      .Returns(Task.CompletedTask)
                      .Verifiable();

            // Act
            int actualValue = await _cacheMock.Object.GetOrCreateAsync(
                key,
                () => expectedValue,
                options: new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });

            // Assert
            Assert.Equal(expectedValue, actualValue);
            _cacheMock.Verify(c => c.GetAsync(key, It.IsAny<CancellationToken>()), Times.Exactly(2));
            _cacheMock.Verify(c => c.SetAsync(
                key,
                It.Is<byte[]>(b => CompareBytes(b, serializedValue)),
                It.Is<DistributedCacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(5)),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task GetOrCreateAsync_CustomSerializer_IsUsed()
        {
            // Arrange
            string key = "test_key";
            string expectedValue = "custom_serialized_value";
            byte[] serializedValue = Encoding.UTF8.GetBytes(expectedValue);

            var customSerializerMock = new Mock<ISerializer>();
            customSerializerMock.Setup(s => s.Deserialize<string>(It.IsAny<byte[]>()))
                                .Returns(expectedValue);
            customSerializerMock.Setup(s => s.Serialize(It.IsAny<string>()))
                                .Returns(serializedValue);

            _cacheMock.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
                      .ReturnsAsync((byte[])null);
            _cacheMock.Setup(c => c.SetAsync(key, serializedValue,
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
                      .Returns(Task.CompletedTask)
                      .Verifiable();

            // Act
            string actualValue = await _cacheMock.Object.GetOrCreateAsync(
                key,
                () => "new_custom_value",
                customSerializerMock.Object,
                options: null);

            // Assert
            Assert.Equal("new_custom_value", actualValue);
            customSerializerMock.Verify(s => s.Serialize("new_custom_value"), Times.Once);
            customSerializerMock.Verify(s => s.Deserialize<string>(It.IsAny<byte[]>()), Times.Never);
            _cacheMock.Verify(c => c.SetAsync(
                key,
                serializedValue,
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task GetOrCreateAsync_ConcurrentAccess_GetMethodCalledOnce()
        {
            // Arrange
            string key = "test_key";
            string expectedValue = "computed_value";
            int getMethodCallCount = 0;

            var cacheOptions = Options.Create(new MemoryDistributedCacheOptions());
            var cache = new MemoryDistributedCache(cacheOptions);

            Func<CancellationToken, ValueTask<string>> getMethod = async ct =>
            {
                Interlocked.Increment(ref getMethodCallCount);
                await Task.Delay(100); // Simulate some delay
                return expectedValue;
            };

            // Act
            var tasks = Enumerable.Range(0, 10)
                .Select(_ => cache.GetOrCreateAsync(key, getMethod).AsTask())
                .ToArray();

            string[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

            // Assert
            Assert.All(results, result => Assert.Equal(expectedValue, result));
            Assert.Equal(1, getMethodCallCount);
        }

        [Fact]
        public async Task GetOrCreateAsync_DeserializationFailure_RemovesCacheEntry()
        {
            // Arrange
            string key = "test_key";
            byte[] corruptedData = Encoding.UTF8.GetBytes("corrupted_data");
            string expectedValue = "computed_value";

            _cacheMock.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(corruptedData);
            _cacheMock.Setup(c => c.RemoveAsync(key, It.IsAny<CancellationToken>()))
                      .Returns(Task.CompletedTask)
                      .Verifiable();

            // Act & Assert
            var result = await _cacheMock.Object.GetOrCreateAsync<string>(
                key,
                () => expectedValue,
                options: null);

            Assert.Equal(result, expectedValue);
            _cacheMock.Verify(c => c.RemoveAsync(key, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task GetOrCreateAsync_GetMethodThrows_ExceptionPropagated()
        {
            // Arrange
            string key = "test_key";

            _cacheMock.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
                      .ReturnsAsync((byte[])null);

            Func<CancellationToken, ValueTask<string>> getMethod = async ct =>
            {
                await Task.Delay(50);
                throw new InvalidOperationException("Get method failed");
            };

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                var val = await _cacheMock.Object.GetOrCreateAsync(
                    key,
                    getMethod,
                    options: null);
            });

            Assert.Equal("Get method failed", exception.Message);
            _cacheMock.Verify(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task GetOrCreateAsync_DefaultSerializerIsUsed_WhenNoSerializerProvided()
        {
            // Arrange
            string key = "test_key";
            string expectedValue = "default_serialized_value";
            byte[] serializedValue = _defaultSerializer.Serialize(expectedValue);

            _cacheMock.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
                      .ReturnsAsync((byte[])null);
            _cacheMock.Setup(c => c.SetAsync(key, serializedValue,
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
                      .Returns(Task.CompletedTask)
                      .Verifiable();

            // Act
            string actualValue = await _cacheMock.Object.GetOrCreateAsync(
                key,
                () => expectedValue,
                options: null);

            // Assert
            Assert.Equal(expectedValue, actualValue);
            _cacheMock.Verify(c => c.SetAsync(
                key,
                It.Is<byte[]>(b => CompareBytes(b, serializedValue)),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// Helper method to compare two byte arrays for equality.
        /// </summary>
        private bool CompareBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }

    }
}