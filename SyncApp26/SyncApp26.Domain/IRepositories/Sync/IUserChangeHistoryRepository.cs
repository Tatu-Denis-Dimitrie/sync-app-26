using SyncApp26.Domain.Entities;

namespace SyncApp26.Domain.IRepositories
{
    public interface IUserChangeHistoryRepository
    {
        Task<IEnumerable<UserChangeHistory>> GetAllAsync();
        Task<UserChangeHistory?> GetByIdAsync(Guid id);
        Task<IEnumerable<UserChangeHistory>> GetByImportHistoryIdAsync(Guid importHistoryId);
        Task<IEnumerable<UserChangeHistory>> GetByUserIdAsync(Guid userId);
        Task AddAsync(UserChangeHistory userChangeHistory);
        Task DeleteAsync(Guid id);
    }
}