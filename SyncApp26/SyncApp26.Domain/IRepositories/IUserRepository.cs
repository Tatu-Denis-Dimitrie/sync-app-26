using SyncApp26.Domain.Entities;

namespace SyncApp26.Domain.IRepositories
{
    public interface IUserRepository
    {
        Task<User?> GetUserByIdAsync(Guid id);
        Task<IEnumerable<User>> GetAllUsersAsync();
        Task<IEnumerable<User>> GetUsersByDepartmentIdAsync(Guid departmentId);
        Task<IEnumerable<User>> GetUsersAssignedToAsync(Guid assignedToId);
        Task AddUserAsync(User user);
        Task UpdateUserAsync(User user);
        Task DeleteUserAsync(Guid id);
        Task<bool> IsUserLineManagerAsync(Guid userId);
        Task<User?> GetUserByEmailAsync(string email);
        Task<User?> GetUserByPersonalIdAsync(string personalId);
    }
}