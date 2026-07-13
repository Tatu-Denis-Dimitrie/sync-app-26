using Microsoft.EntityFrameworkCore;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Infrastructure.Context;

namespace SyncApp26.Infrastructure.Repositories
{
    public class UserInitialTrainingRepository : IUserInitialTrainingRepository
    {
        private readonly ApplicationDbContext _context;

        public UserInitialTrainingRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<UserInitialTraining?> GetByUserIdAndTypeAsync(Guid userId, string documentType)
        {
            return await _context.UserInitialTrainings
                .FirstOrDefaultAsync(t => t.UserId == userId && t.DocumentType == documentType);
        }

        public async Task AddAsync(UserInitialTraining training)
        {
            await _context.UserInitialTrainings.AddAsync(training);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(UserInitialTraining training)
        {
            _context.UserInitialTrainings.Update(training);
            await _context.SaveChangesAsync();
        }
    }
}
