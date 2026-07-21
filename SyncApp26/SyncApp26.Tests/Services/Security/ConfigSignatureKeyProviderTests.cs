using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SyncApp26.Infrastructure.Services;

namespace SyncApp26.Tests.Services.Security
{
    public class ConfigSignatureKeyProviderTests
    {
        private readonly Mock<IConfiguration> _configurationMock = new();
        private readonly Mock<ILogger<ConfigSignatureKeyProvider>> _loggerMock = new();

        [Fact]
        public async Task GetCurrentKeyAsync_ConfiguredKey_ReturnsItsUtf8Bytes()
        {
            _configurationMock.Setup(c => c["SignatureHmac:DevKey"]).Returns("dev-only-key");

            var provider = new ConfigSignatureKeyProvider(_configurationMock.Object, _loggerMock.Object);
            var key = await provider.GetCurrentKeyAsync();

            Assert.Equal(Encoding.UTF8.GetBytes("dev-only-key"), key);
        }

        [Fact]
        public void Constructor_MissingKey_ThrowsInvalidOperationException()
        {
            _configurationMock.Setup(c => c["SignatureHmac:DevKey"]).Returns((string?)null);

            Assert.Throws<InvalidOperationException>(
                () => new ConfigSignatureKeyProvider(_configurationMock.Object, _loggerMock.Object));
        }

        [Fact]
        public void Constructor_BlankKey_ThrowsInvalidOperationException()
        {
            _configurationMock.Setup(c => c["SignatureHmac:DevKey"]).Returns("   ");

            Assert.Throws<InvalidOperationException>(
                () => new ConfigSignatureKeyProvider(_configurationMock.Object, _loggerMock.Object));
        }
    }
}
