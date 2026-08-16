using SyncApp26.Domain.Entities;

namespace SyncApp26.Domain.IRepositories
{
    public interface IUserRepository
    {
        Task<User?> GetUserByIdAsync(Guid id);
        Task<IEnumerable<User>> GetAllUsersAsync();
        Task<IEnumerable<User>> GetAllUsersIncludingAdminsAsync();
        Task<List<User>> GetAllUsersForComparisonAsync();
        Task<IEnumerable<User>> GetUsersByDepartmentIdAsync(Guid departmentId);
        Task<IEnumerable<User>> GetUsersByWorkSiteIdAsync(Guid workSiteId);
        Task<IEnumerable<User>> GetUsersAssignedToAsync(Guid assignedToId);
        Task AddUserAsync(User user);
        Task AddUsersAsync(IEnumerable<User> users);
        Task UpdateUserAsync(User user);
        Task UpdateUsersAsync(IEnumerable<User> users);
        Task DeleteUserAsync(Guid id);
        Task<bool> IsUserLineManagerAsync(Guid userId);
        Task<User?> GetUserByEmailAsync(string email);
        Task<User?> GetUserByPersonalIdAsync(string personalId);

        Task<(List<User> Items, int TotalCount)> SearchUsersAsync(string? search, int page, int pageSize);

        Task<Role?> GetRoleByNameAsync(string roleName);
        Task<IEnumerable<User>> GetUsersInRoleAsync(string roleName);
        Task<bool> IsInRoleAsync(Guid userId, string roleName);

        Task<List<Role>> GetAllRolesAsync();
        Task<Role?> GetRoleByIdAsync(Guid id);
        Task AddRoleAsync(Role role);
        Task DeleteRoleAsync(Role role);
        Task<bool> RoleHasAssignmentsAsync(Guid roleId);
    }
}