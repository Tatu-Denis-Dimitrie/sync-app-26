using System.Security.Cryptography;
using System.Text;
using Moq;
using SyncApp26.Application.IServices;
using SyncApp26.Application.Services;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;

namespace SyncApp26.Tests.Services.Auth
{
    public class RefreshTokenServiceTests
    {
        private readonly Mock<IRefreshTokenRepository> _repositoryMock = new();

        private RefreshTokenService CreateService() => new(_repositoryMock.Object);

        private static string Hash(string rawToken) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        private static RefreshToken MakeToken(Guid userId, string tokenHash, DateTime expiresAt,
            DateTime? consumedAt = null, DateTime? revokedAt = null, string? replacedByTokenHash = null) => new()
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            ConsumedAt = consumedAt,
            RevokedAt = revokedAt,
            ReplacedByTokenHash = replacedByTokenHash
        };

        // ───────────────────────── IssueAsync ─────────────────────────

        [Fact]
        public async Task IssueAsync_HappyPath_PersistsHashedTokenAndReturnsRawValue()
        {
            var service = CreateService();
            var userId = Guid.NewGuid();
            var expiresAt = DateTime.UtcNow.AddHours(8);
            RefreshToken? added = null;
            _repositoryMock.Setup(r => r.AddAsync(It.IsAny<RefreshToken>()))
                .Callback<RefreshToken>(t => added = t)
                .Returns(Task.CompletedTask);

            var result = await service.IssueAsync(userId, expiresAt);

            Assert.NotNull(added);
            Assert.Equal(userId, added!.UserId);
            Assert.Equal(expiresAt, added.ExpiresAt);
            Assert.Equal(Hash(result.RawToken), added.TokenHash);
            Assert.NotEqual(result.RawToken, added.TokenHash);
            Assert.Equal(expiresAt, result.ExpiresAt);
            _repositoryMock.Verify(r => r.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task IssueAsync_AlsoPurgesExpiredTokens()
        {
            var service = CreateService();

            await service.IssueAsync(Guid.NewGuid(), DateTime.UtcNow.AddHours(8));

            _repositoryMock.Verify(r => r.DeleteExpiredAsync(It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task IssueAsync_PurgeThrows_StillReturnsIssuedToken()
        {
            // Purge is best-effort maintenance - it must never block issuing a session.
            var service = CreateService();
            _repositoryMock.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTime>())).ThrowsAsync(new InvalidOperationException("db down"));

            var result = await service.IssueAsync(Guid.NewGuid(), DateTime.UtcNow.AddHours(8));

            Assert.False(string.IsNullOrEmpty(result.RawToken));
        }

        // ───────────────────────── RotateAsync ─────────────────────────

        [Fact]
        public async Task RotateAsync_TokenNotFound_ReturnsNotFound()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>())).ReturnsAsync((RefreshToken?)null);

            var result = await service.RotateAsync("bogus-token");

            Assert.Equal(RefreshOutcome.NotFound, result.Outcome);
        }

        [Fact]
        public async Task RotateAsync_TokenExpired_ReturnsExpiredWithoutRevoking()
        {
            var service = CreateService();
            var userId = Guid.NewGuid();
            var raw = "raw-token";
            _repositoryMock.Setup(r => r.GetByTokenHashAsync(Hash(raw)))
                .ReturnsAsync(MakeToken(userId, Hash(raw), DateTime.UtcNow.AddMinutes(-1)));

            var result = await service.RotateAsync(raw);

            Assert.Equal(RefreshOutcome.Expired, result.Outcome);
            _repositoryMock.Verify(r => r.GetActiveForUserAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task RotateAsync_TokenRevoked_ReturnsRevoked()
        {
            var service = CreateService();
            var userId = Guid.NewGuid();
            var raw = "raw-token";
            _repositoryMock.Setup(r => r.GetByTokenHashAsync(Hash(raw)))
                .ReturnsAsync(MakeToken(userId, Hash(raw), DateTime.UtcNow.AddHours(1), revokedAt: DateTime.UtcNow.AddMinutes(-5)));

            var result = await service.RotateAsync(raw);

            Assert.Equal(RefreshOutcome.Revoked, result.Outcome);
        }

        [Fact]
        public async Task RotateAsync_HappyPath_ConsumesOldAndIssuesSuccessorInheritingExpiry()
        {
            var service = CreateService();
            var userId = Guid.NewGuid();
            var raw = "raw-token";
            var expiresAt = DateTime.UtcNow.AddHours(3);
            var existing = MakeToken(userId, Hash(raw), expiresAt);
            _repositoryMock.Setup(r => r.GetByTokenHashAsync(Hash(raw))).ReturnsAsync(existing);
            RefreshToken? added = null;
            _repositoryMock.Setup(r => r.AddAsync(It.IsAny<RefreshToken>()))
                .Callback<RefreshToken>(t => added = t)
                .Returns(Task.CompletedTask);

            var result = await service.RotateAsync(raw);

            Assert.Equal(RefreshOutcome.Success, result.Outcome);
            Assert.NotNull(existing.ConsumedAt);
            Assert.NotNull(added);
            Assert.Equal(userId, added!.UserId);
            Assert.Equal(expiresAt, added.ExpiresAt); // inherited, not extended
            Assert.Equal(existing.ReplacedByTokenHash, added.TokenHash);
            Assert.Equal(Hash(result.Token!.RawToken), added.TokenHash);
            Assert.Equal(expiresAt, result.Token.ExpiresAt);
        }

        [Fact]
        public async Task RotateAsync_ConsumedWithinGraceWindow_MintsSiblingWithoutRevokingChain()
        {
            // Two tabs racing to refresh at once both present the same pre-rotation token - the
            // second one to arrive must not be treated as theft.
            var service = CreateService();
            var userId = Guid.NewGuid();
            var raw = "raw-token";
            var firstSuccessorHash = "already-rotated-successor-hash";
            var existing = MakeToken(userId, Hash(raw), DateTime.UtcNow.AddHours(1),
                consumedAt: DateTime.UtcNow.AddSeconds(-5), replacedByTokenHash: firstSuccessorHash);
            _repositoryMock.Setup(r => r.GetByTokenHashAsync(Hash(raw))).ReturnsAsync(existing);

            var result = await service.RotateAsync(raw);

            Assert.Equal(RefreshOutcome.Success, result.Outcome);
            // The original chain pointer must survive untouched - only the FIRST rotation owns it.
            Assert.Equal(firstSuccessorHash, existing.ReplacedByTokenHash);
            _repositoryMock.Verify(r => r.GetActiveForUserAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task RotateAsync_ConsumedOutsideGraceWindow_TreatsAsReuseAndRevokesEveryActiveSession()
        {
            var service = CreateService();
            var userId = Guid.NewGuid();
            var raw = "raw-token";
            var existing = MakeToken(userId, Hash(raw), DateTime.UtcNow.AddHours(1),
                consumedAt: DateTime.UtcNow.AddSeconds(-31), replacedByTokenHash: "some-successor-hash");
            _repositoryMock.Setup(r => r.GetByTokenHashAsync(Hash(raw))).ReturnsAsync(existing);
            var otherActiveToken = MakeToken(userId, "other-active-hash", DateTime.UtcNow.AddHours(2));
            _repositoryMock.Setup(r => r.GetActiveForUserAsync(userId)).ReturnsAsync(new List<RefreshToken> { otherActiveToken });

            var result = await service.RotateAsync(raw);

            Assert.Equal(RefreshOutcome.Reused, result.Outcome);
            Assert.Equal(userId, result.UserId);
            Assert.Null(result.Token);
            Assert.NotNull(otherActiveToken.RevokedAt);
            _repositoryMock.Verify(r => r.AddAsync(It.IsAny<RefreshToken>()), Times.Never);
        }

        // ───────────────────────── RevokeAsync ─────────────────────────

        [Fact]
        public async Task RevokeAsync_HappyPath_SetsRevokedAt()
        {
            var service = CreateService();
            var raw = "raw-token";
            var existing = MakeToken(Guid.NewGuid(), Hash(raw), DateTime.UtcNow.AddHours(1));
            _repositoryMock.Setup(r => r.GetByTokenHashAsync(Hash(raw))).ReturnsAsync(existing);

            await service.RevokeAsync(raw);

            Assert.NotNull(existing.RevokedAt);
            _repositoryMock.Verify(r => r.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task RevokeAsync_TokenNotFound_NoOp()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>())).ReturnsAsync((RefreshToken?)null);

            await service.RevokeAsync("bogus-token");

            _repositoryMock.Verify(r => r.SaveChangesAsync(), Times.Never);
        }

        [Fact]
        public async Task RevokeAsync_AlreadyRevoked_NoOp()
        {
            var service = CreateService();
            var raw = "raw-token";
            var existing = MakeToken(Guid.NewGuid(), Hash(raw), DateTime.UtcNow.AddHours(1), revokedAt: DateTime.UtcNow.AddMinutes(-1));
            _repositoryMock.Setup(r => r.GetByTokenHashAsync(Hash(raw))).ReturnsAsync(existing);

            await service.RevokeAsync(raw);

            _repositoryMock.Verify(r => r.SaveChangesAsync(), Times.Never);
        }

        // ───────────────────────── RevokeAllForUserAsync ─────────────────────────

        [Fact]
        public async Task RevokeAllForUserAsync_HappyPath_RevokesEveryActiveToken()
        {
            var service = CreateService();
            var userId = Guid.NewGuid();
            var tokenA = MakeToken(userId, "hash-a", DateTime.UtcNow.AddHours(1));
            var tokenB = MakeToken(userId, "hash-b", DateTime.UtcNow.AddHours(2));
            _repositoryMock.Setup(r => r.GetActiveForUserAsync(userId)).ReturnsAsync(new List<RefreshToken> { tokenA, tokenB });

            await service.RevokeAllForUserAsync(userId);

            Assert.NotNull(tokenA.RevokedAt);
            Assert.NotNull(tokenB.RevokedAt);
            _repositoryMock.Verify(r => r.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task RevokeAllForUserAsync_NoActiveTokens_NoOp()
        {
            var service = CreateService();
            var userId = Guid.NewGuid();
            _repositoryMock.Setup(r => r.GetActiveForUserAsync(userId)).ReturnsAsync(new List<RefreshToken>());

            await service.RevokeAllForUserAsync(userId);

            _repositoryMock.Verify(r => r.SaveChangesAsync(), Times.Never);
        }
    }
}
