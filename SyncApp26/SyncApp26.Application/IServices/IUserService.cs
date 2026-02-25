using SyncApp26.Domain.Entities;

namespace SyncApp26.Application.IServices
{
    public interface IUserService
    {
        Task<User?> GetUserByIdAsync(Guid userId);
        Task<IEnumerable<User>> GetAllUsersAsync();
        Task<IEnumerable<User>> GetUsersByDepartmentIdAsync(Guid departmentId);
        Task<IEnumerable<User>> GetUsersAssignedToAsync(Guid assignedToId);
        Task AddUserAsync(User user);
        Task UpdateUserAsync(User user);
        Task DeleteUserAsync(Guid userId);
        Task<User?> GetUserByPersonalIdAsync(string personalId);
        Task<User?> GetUserByEmailAsync(string email);
    }
}