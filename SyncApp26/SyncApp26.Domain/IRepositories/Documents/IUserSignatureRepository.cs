using SyncApp26.Domain.Entities;

namespace SyncApp26.Domain.IRepositories
{
    public interface IUserSignatureRepository
    {
        Task<UserSignature?> GetByUserIdAsync(Guid userId);
        Task AddAsync(UserSignature signature);
        Task UpdateAsync(UserSignature signature);
        Task AddHistoryAsync(UserSignatureHistory history);
        Task<IEnumerable<UserSignatureHistory>> GetHistoryByUserIdAsync(Guid userId);
    }
}
