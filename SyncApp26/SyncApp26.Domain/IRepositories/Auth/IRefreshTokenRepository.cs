using SyncApp26.Domain.Entities;

namespace SyncApp26.Domain.IRepositories
{
    public interface IRefreshTokenRepository
    {
        Task<RefreshToken?> GetByTokenHashAsync(string tokenHash);

        // "Active" = not yet consumed and not yet revoked.
        Task<List<RefreshToken>> GetActiveForUserAsync(Guid userId);

        // Tracks but doesn't save - lets callers batch it with another change (e.g. rotation) in one SaveChangesAsync.
        Task AddAsync(RefreshToken token);

        Task SaveChangesAsync();

        Task<int> DeleteExpiredAsync(DateTime olderThan);
    }
}
