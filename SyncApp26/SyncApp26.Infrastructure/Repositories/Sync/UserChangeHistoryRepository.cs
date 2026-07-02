using Microsoft.EntityFrameworkCore;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Infrastructure.Context;

namespace SyncApp26.Infrastructure.Repositories
{
    public class UserChangeHistoryRepository : IUserChangeHistoryRepository
    {
        private readonly ApplicationDbContext _context;

        public UserChangeHistoryRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<UserChangeHistory>> GetAllAsync()
        {
            return await _context.UserChangeHistories
                .Include(c => c.ImportHistory)
                .ToListAsync();
        }

        public async Task<UserChangeHistory?> GetByIdAsync(Guid id)
        {
            return await _context.UserChangeHistories
                .Include(c => c.ImportHistory)
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<IEnumerable<UserChangeHistory>> GetByImportHistoryIdAsync(Guid importHistoryId)
        {
            return await _context.UserChangeHistories
                .Include(c => c.ImportHistory)
                .Where(c => c.ImportHistoryId == importHistoryId)
                .ToListAsync();
        }

        public async Task<IEnumerable<UserChangeHistory>> GetByUserIdAsync(Guid userId)
        {
            return await _context.UserChangeHistories
                .Include(c => c.ImportHistory)
                .Where(c => c.UserId == userId)
                .ToListAsync();
        }

        public async Task AddAsync(UserChangeHistory userChangeHistory)
        {
            await _context.UserChangeHistories.AddAsync(userChangeHistory);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            var conflict = await _context.UserChangeHistories.FindAsync(id);
            if (conflict != null)
            {
                _context.UserChangeHistories.Remove(conflict);
                await _context.SaveChangesAsync();
            }
        }
    }
}