using SyncApp26.Domain.Entities;

namespace SyncApp26.Application.IServices
{
    public interface IUserInitialTrainingService
    {
        Task<UserInitialTraining?> GetByUserIdAndTypeAsync(Guid userId, string documentType);
        Task AddAsync(UserInitialTraining training);
        Task UpdateAsync(UserInitialTraining training);
    }
}
