using SyncApp26.Domain.Entities;

namespace SyncApp26.Application.IServices
{
    public interface IWorkSiteService
    {
        Task<WorkSite?> GetWorkSiteByIdAsync(Guid workSiteId);
        Task<IEnumerable<WorkSite>> GetAllWorkSitesAsync();
        Task AddWorkSiteAsync(WorkSite workSite);
        Task UpdateWorkSiteAsync(WorkSite workSite);
        Task<IEnumerable<WorkSite>> GetDeletedWorkSitesAsync();
        Task<WorkSite?> GetDeletedWorkSiteByIdAsync(Guid id);
    }
}
