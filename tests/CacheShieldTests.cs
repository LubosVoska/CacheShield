using Microsoft.Extensions.Caching.Distributed;
using Moq;

namespace CacheShield.Tests
{
    public class CacheShieldTests
    {
        [Fact]
        public async Task GetAsync_ReturnsCachedValue_WhenValueExists()
        {
            // Arrange
            var cacheMock = new Mock<IDistributedCache>();
            var key = "testKey";
            var cachedValue = System.Text.Encoding.UTF8.GetBytes("\"cachedValue\"");
            cacheMock.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>())).ReturnsAsync(cachedValue);

            // Act
            var result = await cacheMock.Object.GetAsync<string>(key, () => "newValue");

            // Assert
            Assert.Equal("cachedValue", result);
            cacheMock.Verify(c => c.GetAsync(key, It.IsAny<CancellationToken>()), Times.Once);
            cacheMock.VerifyNoOtherCalls();
        }
    }
}