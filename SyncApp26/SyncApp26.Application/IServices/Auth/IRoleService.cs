using SyncApp26.Shared.DTOs.Request.User;
using SyncApp26.Shared.DTOs.Response.User;

namespace SyncApp26.Application.IServices
{
    public interface IRoleService
    {
        Task<List<RoleResponseDTO>> GetAllRolesAsync();

        /// <summary>Throws ArgumentException when the name is missing or already taken.</summary>
        Task<RoleResponseDTO> CreateRoleAsync(CreateRoleRequestDTO request);

        /// <summary>Throws ArgumentException when the role doesn't exist, is a built-in system role,
        /// or is still assigned to at least one user.</summary>
        Task DeleteRoleAsync(Guid id);
    }
}
