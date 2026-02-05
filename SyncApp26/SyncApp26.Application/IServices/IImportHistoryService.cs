using SyncApp26.Domain.Entities;

namespace SyncApp26.Application.IServices
{
    public interface IImportHistoryService
    {
        Task<IEnumerable<ImportHistory>> GetAllImportHistoriesAsync();
        Task<ImportHistory?> GetImportHistoryByIdAsync(Guid id);
        Task AddImportHistoryAsync(ImportHistory importHistory);
        Task DeleteImportHistoryAsync(Guid id);
    }
}