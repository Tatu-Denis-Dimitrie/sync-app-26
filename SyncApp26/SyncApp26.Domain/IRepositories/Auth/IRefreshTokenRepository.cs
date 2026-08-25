using SyncApp26.Domain.Entities;

namespace SyncApp26.Domain.IRepositories
{
    public interface IRefreshTokenRepository
    {
        Task<RefreshToken?> GetByTokenHashAsync(string tokenHash);

        // "Active" = not yet consumed (rotated forward) and not yet revoked. Used for the
        // reuse-detection response, which revokes every session tip a user currently has.
        Task<List<RefreshToken>> GetActiveForUserAsync(Guid userId);

        // Tracks the new token but does not save - callers that mutate an existing tracked token in
        // the same logical operation (e.g. rotation) need both changes flushed in one SaveChangesAsync.
        Task AddAsync(RefreshToken token);

        Task SaveChangesAsync();

        Task<int> DeleteExpiredAsync(DateTime olderThan);
    }
}
