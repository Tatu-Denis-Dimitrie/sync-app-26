using Microsoft.EntityFrameworkCore;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Infrastructure.Context;

namespace SyncApp26.Infrastructure.Repositories
{
    public class UserSignatureRepository : IUserSignatureRepository
    {
        private readonly ApplicationDbContext _context;

        public UserSignatureRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<UserSignature?> GetByUserIdAsync(Guid userId)
        {
            return await _context.UserSignatures
                .Where(s => s.UserId == userId && s.RevokedAt == null)
                .FirstOrDefaultAsync();
        }

        public async Task AddAsync(UserSignature signature)
        {
            await _context.UserSignatures.AddAsync(signature);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(UserSignature signature)
        {
            _context.UserSignatures.Update(signature);
            await _context.SaveChangesAsync();
        }

        public async Task AddHistoryAsync(UserSignatureHistory history)
        {
            await _context.UserSignatureHistories.AddAsync(history);
            await _context.SaveChangesAsync();
        }

        public async Task<IEnumerable<UserSignatureHistory>> GetHistoryByUserIdAsync(Guid userId)
        {
            return await _context.UserSignatureHistories
                .Where(h => h.UserId == userId)
                .OrderByDescending(h => h.CreatedAt)
                .ToListAsync();
        }
    }
}
