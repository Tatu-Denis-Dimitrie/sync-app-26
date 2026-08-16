using Microsoft.EntityFrameworkCore;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Infrastructure.Context;

namespace SyncApp26.Infrastructure.Repositories
{
    public class WorkSiteRepository : IWorkSiteRepository
    {
        private readonly ApplicationDbContext _context;

        public WorkSiteRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<WorkSite?> GetWorkSiteByIdAsync(Guid id)
        {
            return await _context.WorkSites
                .Where(w => w.DeletedAt == null)
                .FirstOrDefaultAsync(w => w.Id == id);
        }

        public async Task<IEnumerable<WorkSite>> GetAllWorkSitesAsync()
        {
            return await _context.WorkSites
                .Where(w => w.DeletedAt == null)
                .ToListAsync();
        }

        public async Task AddWorkSiteAsync(WorkSite workSite)
        {
            await _context.WorkSites.AddAsync(workSite);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateWorkSiteAsync(WorkSite workSite)
        {
            _context.WorkSites.Update(workSite);
            await _context.SaveChangesAsync();
        }

        public async Task<IEnumerable<WorkSite>> GetDeletedWorkSitesAsync()
        {
            return await _context.WorkSites
                .Where(w => w.DeletedAt != null)
                .ToListAsync();
        }

        public async Task<WorkSite?> GetDeletedWorkSiteByIdAsync(Guid id)
        {
            return await _context.WorkSites
                .Where(w => w.DeletedAt != null)
                .FirstOrDefaultAsync(w => w.Id == id);
        }

        public async Task<WorkSite?> GetByNameAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var normalizedName = name.Trim();

            return await _context.WorkSites
                .Where(w => w.DeletedAt == null)
                .FirstOrDefaultAsync(w => w.Name.ToLower() == normalizedName.ToLower());
        }
    }
}
