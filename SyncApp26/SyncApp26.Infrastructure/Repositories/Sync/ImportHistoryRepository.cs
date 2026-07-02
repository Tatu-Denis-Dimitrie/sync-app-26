using SyncApp26.Domain.IRepositories;
using SyncApp26.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using SyncApp26.Domain.Entities;

namespace SyncApp26.Infrastructure.Repositories
{
    public class ImportHistoryRepository : IImportHistoryRepository
    {
        private readonly ApplicationDbContext _context;

        public ImportHistoryRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<ImportHistory>> GetAllAsync()
        {
            return await _context.ImportHistories.ToListAsync();
        }

        public async Task<ImportHistory?> GetByIdAsync(Guid id)
        {
            return await _context.ImportHistories.FindAsync(id);
        }

        public async Task AddAsync(ImportHistory importHistory)
        {
            await _context.ImportHistories.AddAsync(importHistory);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            var history = await _context.ImportHistories.FindAsync(id);
            if (history != null)
            {
                _context.ImportHistories.Remove(history);
                await _context.SaveChangesAsync();
            }
        }
    }
}