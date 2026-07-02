using SyncApp26.Domain.Entities;

namespace SyncApp26.Domain.IRepositories
{
    public interface IUserRepository
    {
        Task<User?> GetUserByIdAsync(Guid id);
        Task<IEnumerable<User>> GetAllUsersAsync();
        Task<List<User>> GetAllUsersForComparisonAsync();
        Task<IEnumerable<User>> GetUsersByDepartmentIdAsync(Guid departmentId);
        Task<IEnumerable<User>> GetUsersAssignedToAsync(Guid assignedToId);
        Task AddUserAsync(User user);
        Task AddUsersAsync(IEnumerable<User> users);
        Task UpdateUserAsync(User user);
        Task UpdateUsersAsync(IEnumerable<User> users);
        Task DeleteUserAsync(Guid id);
        Task<bool> IsUserLineManagerAsync(Guid userId);
        Task<User?> GetUserByEmailAsync(string email);
        Task<User?> GetUserByPersonalIdAsync(string personalId);
        Task<Guid?> GetRoleIdByNameAsync(string roleName);
    }
}