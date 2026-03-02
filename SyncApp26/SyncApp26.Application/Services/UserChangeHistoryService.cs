using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Shared.DTOs.CSV.History;

namespace SyncApp26.Application.Services
{
    public class UserChangeHistoryService : IUserChangeHistoryService
    {
        private readonly IUserChangeHistoryRepository _userChangeHistoryRepository;

        public UserChangeHistoryService(IUserChangeHistoryRepository userChangeHistoryRepository)
        {
            _userChangeHistoryRepository = userChangeHistoryRepository;
        }

        public async Task<IEnumerable<UserChangeHistoryResponseDTO>> GetAllUserChangeHistoriesAsync()
        {
            var userChangeHistories = await _userChangeHistoryRepository.GetAllAsync();

            if(userChangeHistories == null)
            {
                return Enumerable.Empty<UserChangeHistoryResponseDTO>();
            }

            return userChangeHistories.Select(c => new UserChangeHistoryResponseDTO
            {
                Id = c.Id,
                ImportHistoryId = c.ImportHistoryId,
                ImportDate = c.ImportHistory?.ImportDate,
                ImportFileName = c.ImportHistory?.FileName,
                UserId = c.UserId,
                FieldName = c.FieldName,
                OldValue = c.OldValue,
                NewValue = c.NewValue,
                Status = c.Status,
                CreatedAt = c.CreatedAt
            });
        }

        public async Task<UserChangeHistoryResponseDTO?> GetUserChangeHistoryByIdAsync(Guid id)
        {
            var userChangeHistory = await _userChangeHistoryRepository.GetByIdAsync(id);

            if (userChangeHistory == null)
            {
                return null;
            }

            return new UserChangeHistoryResponseDTO
            {
                Id = userChangeHistory.Id,
                ImportHistoryId = userChangeHistory.ImportHistoryId,
                ImportDate = userChangeHistory.ImportHistory?.ImportDate,
                ImportFileName = userChangeHistory.ImportHistory?.FileName,
                UserId = userChangeHistory.UserId,
                FieldName = userChangeHistory.FieldName,
                OldValue = userChangeHistory.OldValue,
                NewValue = userChangeHistory.NewValue,
                Status = userChangeHistory.Status,
                CreatedAt = userChangeHistory.CreatedAt
            };
        }

        public async Task<IEnumerable<UserChangeHistoryResponseDTO>> GetUserChangeHistoriesByImportHistoryIdAsync(Guid importHistoryId)
        {
            var userChangeHistories = await _userChangeHistoryRepository.GetByImportHistoryIdAsync(importHistoryId);

            if (userChangeHistories == null)
            {
                return Enumerable.Empty<UserChangeHistoryResponseDTO>();
            }

            return userChangeHistories.Select(c => new UserChangeHistoryResponseDTO
            {
                Id = c.Id,
                ImportHistoryId = c.ImportHistoryId,
                ImportDate = c.ImportHistory?.ImportDate,
                ImportFileName = c.ImportHistory?.FileName,
                UserId = c.UserId,
                FieldName = c.FieldName,
                OldValue = c.OldValue,
                NewValue = c.NewValue,
                Status = c.Status,
                CreatedAt = c.CreatedAt
            });
        }

        public async Task<IEnumerable<UserChangeHistoryResponseDTO>> GetUserChangeHistoriesByUserIdAsync(Guid userId)
        {
            var userChangeHistories = await _userChangeHistoryRepository.GetByUserIdAsync(userId);

            if (userChangeHistories == null)
            {
                return Enumerable.Empty<UserChangeHistoryResponseDTO>();
            }

            return userChangeHistories.Select(c => new UserChangeHistoryResponseDTO
            {
                Id = c.Id,
                ImportHistoryId = c.ImportHistoryId,
                ImportDate = c.ImportHistory?.ImportDate,
                ImportFileName = c.ImportHistory?.FileName,
                UserId = c.UserId,
                FieldName = c.FieldName,
                OldValue = c.OldValue,
                NewValue = c.NewValue,
                Status = c.Status,
                CreatedAt = c.CreatedAt
            });
        }

        public async Task AddUserChangeHistoryAsync(UserChangeHistory userChangeHistory)
        {
            var conflict = new UserChangeHistory
            {
                Id = Guid.NewGuid(),
                UserId = userChangeHistory.UserId,
                ImportHistoryId = userChangeHistory.ImportHistoryId,
                FieldName = userChangeHistory.FieldName,
                OldValue = userChangeHistory.OldValue,
                NewValue = userChangeHistory.NewValue,
                Status = userChangeHistory.Status,
                CreatedAt = userChangeHistory.CreatedAt == default ? DateTime.UtcNow : userChangeHistory.CreatedAt
            };

            await _userChangeHistoryRepository.AddAsync(conflict);
        }

        public async Task DeleteUserChangeHistoryAsync(Guid id)
        {
            await _userChangeHistoryRepository.DeleteAsync(id);
        }
    }
}