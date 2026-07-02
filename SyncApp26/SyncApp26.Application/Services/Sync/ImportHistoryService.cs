using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories; 
using SyncApp26.Application.IServices;

namespace SyncApp26.Application.Services
{
    public class ImportHistoryService : IImportHistoryService
    {
        private readonly IImportHistoryRepository _importHistoryRepository;

        public ImportHistoryService(IImportHistoryRepository importHistoryRepository)
        {
            _importHistoryRepository = importHistoryRepository;
        }

        public async Task<IEnumerable<ImportHistory>> GetAllImportHistoriesAsync()
        {
            return await _importHistoryRepository.GetAllAsync();
        }

        public async Task<ImportHistory?> GetImportHistoryByIdAsync(Guid id)
        {
            return await _importHistoryRepository.GetByIdAsync(id);
        }

        public async Task AddImportHistoryAsync(ImportHistory importHistory)
        {
            await _importHistoryRepository.AddAsync(importHistory);
        }

        public async Task DeleteImportHistoryAsync(Guid id)
        {
            await _importHistoryRepository.DeleteAsync(id);
        }
    }
}