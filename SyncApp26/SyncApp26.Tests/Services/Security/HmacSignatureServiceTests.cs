using System.Text;
using Moq;
using SyncApp26.Application.IServices;
using SyncApp26.Infrastructure.Services;

namespace SyncApp26.Tests.Services.Security
{
    public class HmacSignatureServiceTests
    {
        private static HmacSignatureService CreateService(string key = "test-signing-key")
        {
            var keyProviderMock = new Mock<ISignatureKeyProvider>();
            keyProviderMock.Setup(p => p.GetCurrentKeyAsync()).ReturnsAsync(Encoding.UTF8.GetBytes(key));
            return new HmacSignatureService(keyProviderMock.Object);
        }

        [Fact]
        public async Task ComputeHmacAsync_SameInputAndKey_ProducesIdenticalHmac()
        {
            var service = CreateService();

            var first = await service.ComputeHmacAsync("canonical-data");
            var second = await service.ComputeHmacAsync("canonical-data");

            Assert.Equal(first, second);
        }

        [Fact]
        public async Task ComputeHmacAsync_DifferentKeys_ProduceDifferentHmac()
        {
            var serviceA = CreateService("key-a");
            var serviceB = CreateService("key-b");

            var hmacA = await serviceA.ComputeHmacAsync("canonical-data");
            var hmacB = await serviceB.ComputeHmacAsync("canonical-data");

            Assert.NotEqual(hmacA, hmacB);
        }

        [Fact]
        public async Task VerifyHmacAsync_UnmodifiedInput_ReturnsTrue()
        {
            var service = CreateService();
            var hmac = await service.ComputeHmacAsync("canonical-data");

            var isValid = await service.VerifyHmacAsync("canonical-data", hmac);

            Assert.True(isValid);
        }

        [Fact]
        public async Task VerifyHmacAsync_OneCharacterChangedInInput_ReturnsFalse()
        {
            var service = CreateService();
            var hmac = await service.ComputeHmacAsync("canonical-data");

            var isValid = await service.VerifyHmacAsync("canonical-datb", hmac);

            Assert.False(isValid);
        }

        [Fact]
        public async Task VerifyHmacAsync_TamperedHmacValue_ReturnsFalse()
        {
            var service = CreateService();
            var hmac = await service.ComputeHmacAsync("canonical-data");
            var tamperedHmac = hmac[..^1] + (hmac[^1] == 'a' ? 'b' : 'a');

            var isValid = await service.VerifyHmacAsync("canonical-data", tamperedHmac);

            Assert.False(isValid);
        }

        [Fact]
        public async Task VerifyHmacAsync_PlainShaHashInsteadOfHmac_ReturnsFalse()
        {
            // Demonstrates the property that actually matters: without the key, an attacker
            // who can read/write the data cannot produce a value this service accepts —
            // a plain, unkeyed hash of the same input is not a valid substitute.
            var service = CreateService();
            var plainHash = System.Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("canonical-data"))).ToLowerInvariant();

            var isValid = await service.VerifyHmacAsync("canonical-data", plainHash);

            Assert.False(isValid);
        }
    }
}
