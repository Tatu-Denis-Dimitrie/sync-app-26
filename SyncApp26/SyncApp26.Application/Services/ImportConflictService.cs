using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Shared.DTOs.CSV.History;

namespace SyncApp26.Application.Services
{
    public class ImportConflictService : IImportConflictService
    {
        private readonly IImportConflictRepository _importConflictRepository;

        public ImportConflictService(IImportConflictRepository importConflictRepository)
        {
            _importConflictRepository = importConflictRepository;
        }

        public async Task<IEnumerable<ImportConflictResponseDTO>> GetAllImportConflictsAsync()
        {
            var conflicts = await _importConflictRepository.GetAllAsync();

            if(conflicts == null)
            {
                return Enumerable.Empty<ImportConflictResponseDTO>();
            }

            return conflicts.Select(c => new ImportConflictResponseDTO
            {
                Id = c.Id,
                ImportHistoryId = c.ImportHistoryId,
                ImportDate = c.ImportHistory?.ImportDate,
                ImportFileName = c.ImportHistory?.FileName,
                UserId = c.UserId,
                FieldName = c.FieldName,
                OldValue = c.OldValue,
                NewValue = c.NewValue,
                Status = c.Status
            });
        }

        public async Task<ImportConflictResponseDTO?> GetImportConflictByIdAsync(Guid id)
        {
            var conflict = await _importConflictRepository.GetByIdAsync(id);

            if (conflict == null)
            {
                return null;
            }

            return new ImportConflictResponseDTO
            {
                Id = conflict.Id,
                ImportHistoryId = conflict.ImportHistoryId,
                ImportDate = conflict.ImportHistory?.ImportDate,
                ImportFileName = conflict.ImportHistory?.FileName,
                UserId = conflict.UserId,
                FieldName = conflict.FieldName,
                OldValue = conflict.OldValue,
                NewValue = conflict.NewValue,
                Status = conflict.Status
            };
        }

        public async Task<IEnumerable<ImportConflictResponseDTO>> GetImportConflictsByImportHistoryIdAsync(Guid importHistoryId)
        {
            var conflicts = await _importConflictRepository.GetByImportHistoryIdAsync(importHistoryId);

            if (conflicts == null)
            {
                return Enumerable.Empty<ImportConflictResponseDTO>();
            }

            return conflicts.Select(c => new ImportConflictResponseDTO
            {
                Id = c.Id,
                ImportHistoryId = c.ImportHistoryId,
                ImportDate = c.ImportHistory?.ImportDate,
                ImportFileName = c.ImportHistory?.FileName,
                UserId = c.UserId,
                FieldName = c.FieldName,
                OldValue = c.OldValue,
                NewValue = c.NewValue,
                Status = c.Status
            });
        }

        public async Task<IEnumerable<ImportConflictResponseDTO>> GetImportConflictsByUserIdAsync(Guid userId)
        {
            var conflicts = await _importConflictRepository.GetByUserIdAsync(userId);

            if (conflicts == null)
            {
                return Enumerable.Empty<ImportConflictResponseDTO>();
            }

            return conflicts.Select(c => new ImportConflictResponseDTO
            {
                Id = c.Id,
                ImportHistoryId = c.ImportHistoryId,
                ImportDate = c.ImportHistory?.ImportDate,
                ImportFileName = c.ImportHistory?.FileName,
                UserId = c.UserId,
                FieldName = c.FieldName,
                OldValue = c.OldValue,
                NewValue = c.NewValue,
                Status = c.Status
            });
        }

        public async Task AddImportConflictAsync(ImportConflict importConflict)
        {
            var conflict = new ImportConflict
            {
                Id = Guid.NewGuid(),
                UserId = importConflict.UserId,
                ImportHistoryId = importConflict.ImportHistoryId,
                FieldName = importConflict.FieldName,
                OldValue = importConflict.OldValue,
                NewValue = importConflict.NewValue,
                Status = importConflict.Status
            };

            await _importConflictRepository.AddAsync(conflict);
        }

        public async Task DeleteImportConflictAsync(Guid id)
        {
            await _importConflictRepository.DeleteAsync(id);
        }
    }
}