using SyncApp26.Domain.Entities;
using SyncApp26.Shared.DTOs.CSV.History;

namespace SyncApp26.Application.IServices
{
    public interface IUserChangeHistoryService
    {
        Task<IEnumerable<UserChangeHistoryResponseDTO>> GetAllUserChangeHistoriesAsync();
        Task<UserChangeHistoryResponseDTO?> GetUserChangeHistoryByIdAsync(Guid id);
        Task<IEnumerable<UserChangeHistoryResponseDTO>> GetUserChangeHistoriesByImportHistoryIdAsync(Guid importHistoryId);
        Task<IEnumerable<UserChangeHistoryResponseDTO>> GetUserChangeHistoriesByUserIdAsync(Guid userId);
        Task<(List<UserChangeHistoryResponseDTO> Items, int TotalCount)> GetUserChangeHistoriesByUserIdPageAsync(Guid userId, int page, int pageSize);
        Task AddUserChangeHistoryAsync(UserChangeHistory userChangeHistory);
        Task DeleteUserChangeHistoryAsync(Guid id);
    }
}