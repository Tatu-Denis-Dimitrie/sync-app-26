using SyncApp26.Domain.IRepositories;
using SyncApp26.Infrastructure.Context;
using SyncApp26.Domain.Entities;

namespace SyncApp26.Infrastructure.Repositories
{
    public class ImpersonationLogRepository : IImpersonationLogRepository
    {
        private readonly ApplicationDbContext _context;

        public ImpersonationLogRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(ImpersonationLog log)
        {
            await _context.ImpersonationLogs.AddAsync(log);
            await _context.SaveChangesAsync();
        }
    }
}
