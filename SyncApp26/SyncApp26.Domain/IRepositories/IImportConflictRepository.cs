using SyncApp26.Domain.Entities;

namespace SyncApp26.Domain.IRepositories
{
    public interface IImportConflictRepository
    {
        Task<IEnumerable<ImportConflict>> GetAllAsync();
        Task<ImportConflict?> GetByIdAsync(Guid id);
        Task<IEnumerable<ImportConflict>> GetByImportHistoryIdAsync(Guid importHistoryId);
        Task<IEnumerable<ImportConflict>> GetByUserIdAsync(Guid userId);
        Task AddAsync(ImportConflict importConflict);
        Task DeleteAsync(Guid id);
    }
}