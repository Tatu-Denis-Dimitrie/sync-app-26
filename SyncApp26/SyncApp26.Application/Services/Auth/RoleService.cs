using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Shared.DTOs.Request.User;
using SyncApp26.Shared.DTOs.Response.User;

namespace SyncApp26.Application.Services
{
    public class RoleService : IRoleService
    {
        private readonly IUserRepository _userRepository;

        public RoleService(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task<List<RoleResponseDTO>> GetAllRolesAsync()
        {
            var roles = await _userRepository.GetAllRolesAsync();
            return roles.Select(MapToDTO).ToList();
        }

        public async Task<RoleResponseDTO> CreateRoleAsync(CreateRoleRequestDTO request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new ArgumentException("Role name is required.");

            var name = request.Name.Trim();
            var existing = await _userRepository.GetRoleByNameAsync(name);
            if (existing != null)
                throw new ArgumentException($"A role named '{name}' already exists.");

            // Custom roles created here carry no built-in meaning - something in code has to check
            // for the name before this role grants any actual permission (see SyncApp26.Domain.Enums.Roles).
            var role = new Role
            {
                Id = Guid.NewGuid(),
                Name = name,
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                IsSystem = false,
                CreatedAt = DateTime.UtcNow
            };

            await _userRepository.AddRoleAsync(role);
            return MapToDTO(role);
        }

        public async Task DeleteRoleAsync(Guid id)
        {
            var role = await _userRepository.GetRoleByIdAsync(id);
            if (role == null)
                throw new ArgumentException("Role not found.");

            // System roles are what [Authorize(Roles = ...)] checks by name - deleting one would
            // silently strip authorization from everyone who holds it instead of failing loudly.
            if (role.IsSystem)
                throw new ArgumentException("System roles cannot be deleted.");

            if (await _userRepository.RoleHasAssignmentsAsync(id))
                throw new ArgumentException("Cannot delete a role that is still assigned to users. Remove it from all users first.");

            await _userRepository.DeleteRoleAsync(role);
        }

        private static RoleResponseDTO MapToDTO(Role role) => new()
        {
            Id = role.Id,
            Name = role.Name,
            Description = role.Description,
            IsSystem = role.IsSystem,
            CreatedAt = role.CreatedAt
        };
    }
}
