using Microsoft.EntityFrameworkCore;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Infrastructure.Context;

namespace SyncApp26.Infrastructure.Repositories
{
    public class RefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly ApplicationDbContext _context;

        public RefreshTokenRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash) =>
            _context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        public Task<List<RefreshToken>> GetActiveForUserAsync(Guid userId) =>
            _context.RefreshTokens
                .Where(t => t.UserId == userId && t.ConsumedAt == null && t.RevokedAt == null)
                .ToListAsync();

        public async Task AddAsync(RefreshToken token) =>
            await _context.RefreshTokens.AddAsync(token);

        public Task SaveChangesAsync() => _context.SaveChangesAsync();

        public async Task<int> DeleteExpiredAsync(DateTime olderThan)
        {
            var expired = await _context.RefreshTokens.Where(t => t.ExpiresAt < olderThan).ToListAsync();
            if (expired.Count == 0)
            {
                return 0;
            }

            _context.RefreshTokens.RemoveRange(expired);
            await _context.SaveChangesAsync();
            return expired.Count;
        }
    }
}
