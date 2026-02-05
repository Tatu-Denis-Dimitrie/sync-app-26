using Microsoft.EntityFrameworkCore;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Infrastructure.Context;

namespace SyncApp26.Infrastructure.Repositories
{
    public class ImportConflictRepository : IImportConflictRepository
    {
        private readonly ApplicationDbContext _context;

        public ImportConflictRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<ImportConflict>> GetAllAsync()
        {
            return await _context.ImportConflicts.ToListAsync();
        }

        public async Task<ImportConflict?> GetByIdAsync(Guid id)
        {
            return await _context.ImportConflicts.FindAsync(id);
        }

        public async Task<IEnumerable<ImportConflict>> GetByImportHistoryIdAsync(Guid importHistoryId)
        {
            return await _context.ImportConflicts
                .Where(c => c.ImportHistoryId == importHistoryId)
                .ToListAsync();
        }

        public async Task<IEnumerable<ImportConflict>> GetByUserIdAsync(Guid userId)
        {
            return await _context.ImportConflicts
                .Where(c => c.UserId == userId)
                .ToListAsync();
        }

        public async Task AddAsync(ImportConflict importConflict)
        {
            await _context.ImportConflicts.AddAsync(importConflict);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            var conflict = await _context.ImportConflicts.FindAsync(id);
            if (conflict != null)
            {
                _context.ImportConflicts.Remove(conflict);
                await _context.SaveChangesAsync();
            }
        }
    }
}