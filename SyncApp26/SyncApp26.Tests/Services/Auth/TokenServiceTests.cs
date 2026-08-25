using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using Moq;
using SyncApp26.Application.Services;
using SyncApp26.Domain.Enums;

namespace SyncApp26.Tests.Services.Auth
{
    public class TokenServiceTests
    {
        // HMAC-SHA256 needs a 256-bit key and Encoding.ASCII.GetBytes gives one byte per char, so the
        // secret must be at least 32 characters or SigningCredentials throws at signing time.
        private const string TestSecretKey = "this-is-a-test-secret-key-that-is-long-enough";

        private readonly Mock<IConfiguration> _configurationMock = new();

        private TokenService CreateService(string? secretKey = TestSecretKey)
        {
            _configurationMock.Setup(c => c["JwtSettings:SecretKey"]).Returns(secretKey);
            return new TokenService(_configurationMock.Object);
        }

        [Fact]
        public async Task GenerateTokenAsync_ValidInput_EmitsExpectedClaimsAndNoImpersonatorClaim()
        {
            var service = CreateService();
            var userId = Guid.NewGuid();

            var token = await service.GenerateTokenAsync(userId, "user@test.com", new[] { "LineManager", "SsmOfficer" });

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            // JwtSecurityTokenHandler's default outbound claim map rewrites ClaimTypes.NameIdentifier /
            // Email / Role to the short wire names "nameid" / "email" / "role" when WRITING the token —
            // ReadJwtToken parses the raw wire format, so it sees the short names, not the long URIs.
            // The JwtBearer handler on the receiving side applies the matching inbound map when it
            // builds ClaimsPrincipal, which is why ClaimsPrincipalExtensions.GetUserId() etc. still work.
            Assert.Equal(userId.ToString(), jwt.Claims.Single(c => c.Type == "nameid").Value);
            Assert.Equal("user@test.com", jwt.Claims.Single(c => c.Type == "email").Value);
            Assert.Equal(
                new[] { "LineManager", "SsmOfficer" },
                jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value).ToArray());
            Assert.DoesNotContain(jwt.Claims, c => c.Type == CustomClaimTypes.ImpersonatorId);

            var lifetime = jwt.ValidTo - jwt.ValidFrom;
            Assert.True(Math.Abs((lifetime - TimeSpan.FromMinutes(15)).TotalSeconds) < 60);
        }

        [Fact]
        public async Task GenerateTokenAsync_MissingEmail_ThrowsArgumentException()
        {
            var service = CreateService();

            await Assert.ThrowsAsync<ArgumentException>(
                () => service.GenerateTokenAsync(Guid.NewGuid(), "", Array.Empty<string>()));
        }

        [Fact]
        public async Task GenerateTokenAsync_MissingSecretKey_ThrowsInvalidOperationException()
        {
            var service = CreateService(secretKey: null);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GenerateTokenAsync(Guid.NewGuid(), "user@test.com", Array.Empty<string>()));
        }

        [Fact]
        public async Task GenerateImpersonationTokenAsync_ValidInput_TargetsTargetIdentityAndTagsImpersonator()
        {
            var service = CreateService();
            var targetId = Guid.NewGuid();
            var impersonatorId = Guid.NewGuid();

            var token = await service.GenerateImpersonationTokenAsync(
                targetId, "target@test.com", new[] { "BasicUser" }, impersonatorId);

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            Assert.Equal(targetId.ToString(), jwt.Claims.Single(c => c.Type == "nameid").Value);
            Assert.Equal("target@test.com", jwt.Claims.Single(c => c.Type == "email").Value);
            Assert.Equal("BasicUser", jwt.Claims.Single(c => c.Type == "role").Value);
            Assert.Equal(impersonatorId.ToString(), jwt.Claims.Single(c => c.Type == CustomClaimTypes.ImpersonatorId).Value);

            var lifetime = jwt.ValidTo - jwt.ValidFrom;
            Assert.True(Math.Abs((lifetime - TimeSpan.FromMinutes(30)).TotalSeconds) < 60);
        }

        [Fact]
        public async Task GenerateImpersonationTokenAsync_MissingSecretKey_ThrowsInvalidOperationException()
        {
            var service = CreateService(secretKey: null);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GenerateImpersonationTokenAsync(
                    Guid.NewGuid(), "target@test.com", Array.Empty<string>(), Guid.NewGuid()));
        }
    }
}
