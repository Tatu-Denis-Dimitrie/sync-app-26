using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;

namespace SyncApp26.Application.Services
{
    public class UserInitialTrainingService : IUserInitialTrainingService
    {
        private readonly IUserInitialTrainingRepository _repository;

        public UserInitialTrainingService(IUserInitialTrainingRepository repository)
        {
            _repository = repository;
        }

        public Task<UserInitialTraining?> GetByUserIdAndTypeAsync(Guid userId, string documentType)
        {
            return _repository.GetByUserIdAndTypeAsync(userId, documentType);
        }

        public Task AddAsync(UserInitialTraining training)
        {
            return _repository.AddAsync(training);
        }

        public Task UpdateAsync(UserInitialTraining training)
        {
            return _repository.UpdateAsync(training);
        }
    }
}
