using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Application.IServices;

namespace SyncApp26.Application.Services
{
    public class WorkSiteService : IWorkSiteService
    {
        private readonly IWorkSiteRepository _workSiteRepository;

        public WorkSiteService(IWorkSiteRepository workSiteRepository)
        {
            _workSiteRepository = workSiteRepository;
        }

        public async Task<WorkSite?> GetWorkSiteByIdAsync(Guid workSiteId)
        {
            return await _workSiteRepository.GetWorkSiteByIdAsync(workSiteId);
        }

        public async Task<IEnumerable<WorkSite>> GetAllWorkSitesAsync()
        {
            return await _workSiteRepository.GetAllWorkSitesAsync();
        }

        public async Task AddWorkSiteAsync(WorkSite workSite)
        {
            await _workSiteRepository.AddWorkSiteAsync(workSite);
        }

        public async Task UpdateWorkSiteAsync(WorkSite workSite)
        {
            await _workSiteRepository.UpdateWorkSiteAsync(workSite);
        }

        public async Task<IEnumerable<WorkSite>> GetDeletedWorkSitesAsync()
        {
            return await _workSiteRepository.GetDeletedWorkSitesAsync();
        }

        public async Task<WorkSite?> GetDeletedWorkSiteByIdAsync(Guid id)
        {
            return await _workSiteRepository.GetDeletedWorkSiteByIdAsync(id);
        }
    }
}
