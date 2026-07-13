using SyncApp26.Domain.Entities;

namespace SyncApp26.Domain.IRepositories
{
    public interface IUserInitialTrainingRepository
    {
        Task<UserInitialTraining?> GetByUserIdAndTypeAsync(Guid userId, string documentType);
        Task AddAsync(UserInitialTraining training);
        Task UpdateAsync(UserInitialTraining training);
    }
}
