using SyncApp26.Domain.Entities;
using SyncApp26.Shared.DTOs.CSV.History;

namespace SyncApp26.Application.IServices
{
    public interface IImportConflictService
    {
        Task<IEnumerable<ImportConflictResponseDTO>> GetAllImportConflictsAsync();
        Task<ImportConflictResponseDTO?> GetImportConflictByIdAsync(Guid id);
        Task<IEnumerable<ImportConflictResponseDTO>> GetImportConflictsByImportHistoryIdAsync(Guid importHistoryId);
        Task<IEnumerable<ImportConflictResponseDTO>> GetImportConflictsByUserIdAsync(Guid userId);
        Task AddImportConflictAsync(ImportConflict importConflict);
        Task DeleteImportConflictAsync(Guid id);
    }
}