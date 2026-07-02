using SyncApp26.Domain.Entities;

namespace SyncApp26.Domain.IRepositories
{
    public interface IImportHistoryRepository
    {
        Task<IEnumerable<ImportHistory>> GetAllAsync();
        Task<ImportHistory?> GetByIdAsync(Guid id);
        Task AddAsync(ImportHistory importHistory);
        Task DeleteAsync(Guid id);
    }
}