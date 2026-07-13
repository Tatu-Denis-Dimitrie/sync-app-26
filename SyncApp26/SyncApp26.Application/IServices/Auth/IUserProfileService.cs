using SyncApp26.Domain.Entities;
using SyncApp26.Shared.DTOs.Request.User;
using SyncApp26.Shared.DTOs.Response.User;

namespace SyncApp26.Application.IServices
{
    public interface IUserProfileService
    {
        Task<UserResponseDTO> CreateUserAsync(UserRequestDTO request);
        Task<UserResponseDTO> UpdateUserAsync(User existingUser, UserRequestDTO request);
        Task UpdateSsmSuFormAsync(User user, UpdateUserSSMSUFormDTO dto);
        Task<BulkInitialTrainingResultDTO> ApplyBulkInitialTrainingAsync(BulkInitialTrainingDTO dto, Guid? restrictToAssignedToId);
    }
}
