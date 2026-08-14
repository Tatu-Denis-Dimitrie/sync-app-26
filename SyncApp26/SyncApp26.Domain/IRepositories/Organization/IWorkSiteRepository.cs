using SyncApp26.Domain.Entities;

namespace SyncApp26.Domain.IRepositories
{
    public interface IWorkSiteRepository
    {
        Task<WorkSite?> GetWorkSiteByIdAsync(Guid id);
        Task<IEnumerable<WorkSite>> GetAllWorkSitesAsync();
        Task AddWorkSiteAsync(WorkSite workSite);
        Task UpdateWorkSiteAsync(WorkSite workSite);
        Task<IEnumerable<WorkSite>> GetDeletedWorkSitesAsync();
        Task<WorkSite?> GetDeletedWorkSiteByIdAsync(Guid id);
        Task<WorkSite?> GetByNameAsync(string name);
    }
}
