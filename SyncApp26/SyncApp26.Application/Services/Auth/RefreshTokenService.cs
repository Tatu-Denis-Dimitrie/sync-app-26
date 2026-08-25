using System.Security.Cryptography;
using System.Text;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;

namespace SyncApp26.Application.Services
{
    public class RefreshTokenService : IRefreshTokenService
    {
        // Two tabs refreshing at (almost) the same moment both read the same pre-rotation cookie;
        // the second request to arrive would otherwise look identical to a stolen, already-used
        // token. This window is how long a just-consumed token is still treated as a benign race
        // rather than theft.
        private static readonly TimeSpan GraceWindow = TimeSpan.FromSeconds(30);

        private readonly IRefreshTokenRepository _repository;

        public RefreshTokenService(IRefreshTokenRepository repository)
        {
            _repository = repository;
        }

        public async Task<IssuedRefreshToken> IssueAsync(Guid userId, DateTime expiresAt)
        {
            var (raw, hash) = GenerateToken();
            await _repository.AddAsync(new RefreshToken { UserId = userId, TokenHash = hash, ExpiresAt = expiresAt });
            await _repository.SaveChangesAsync();

            try
            {
                await _repository.DeleteExpiredAsync(DateTime.UtcNow.AddDays(-1));
            }
            catch
            {
                // Best-effort maintenance - a purge failure must never block issuing a session.
            }

            return new IssuedRefreshToken { RawToken = raw, ExpiresAt = expiresAt };
        }

        public async Task<RefreshResult> RotateAsync(string rawToken)
        {
            var existing = await _repository.GetByTokenHashAsync(HashToken(rawToken));
            if (existing == null)
            {
                return new RefreshResult { Outcome = RefreshOutcome.NotFound };
            }

            if (existing.ExpiresAt <= DateTime.UtcNow)
            {
                return new RefreshResult { Outcome = RefreshOutcome.Expired };
            }

            if (existing.RevokedAt != null)
            {
                return new RefreshResult { Outcome = RefreshOutcome.Revoked };
            }

            if (existing.ConsumedAt != null)
            {
                var withinGraceWindow = DateTime.UtcNow - existing.ConsumedAt.Value <= GraceWindow;
                if (!withinGraceWindow)
                {
                    await RevokeAllForUserAsync(existing.UserId);
                    return new RefreshResult { Outcome = RefreshOutcome.Reused, UserId = existing.UserId };
                }
                // Within the grace window: fall through and mint another successor without
                // re-touching ConsumedAt/ReplacedByTokenHash, which already point at the first one.
            }
            else
            {
                existing.ConsumedAt = DateTime.UtcNow;
            }

            var (rawNew, hashNew) = GenerateToken();
            var successor = new RefreshToken { UserId = existing.UserId, TokenHash = hashNew, ExpiresAt = existing.ExpiresAt };

            existing.ReplacedByTokenHash ??= hashNew;

            await _repository.AddAsync(successor);
            await _repository.SaveChangesAsync();

            return new RefreshResult
            {
                Outcome = RefreshOutcome.Success,
                UserId = existing.UserId,
                Token = new IssuedRefreshToken { RawToken = rawNew, ExpiresAt = successor.ExpiresAt }
            };
        }

        public async Task RevokeAsync(string rawToken)
        {
            var existing = await _repository.GetByTokenHashAsync(HashToken(rawToken));
            if (existing == null || existing.RevokedAt != null)
            {
                return;
            }

            existing.RevokedAt = DateTime.UtcNow;
            await _repository.SaveChangesAsync();
        }

        public async Task RevokeAllForUserAsync(Guid userId)
        {
            var active = await _repository.GetActiveForUserAsync(userId);
            if (active.Count == 0)
            {
                return;
            }

            var now = DateTime.UtcNow;
            foreach (var token in active)
            {
                token.RevokedAt = now;
            }

            await _repository.SaveChangesAsync();
        }

        private static (string Raw, string Hash) GenerateToken()
        {
            var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
            return (raw, HashToken(raw));
        }

        private static string HashToken(string rawToken) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }
}
