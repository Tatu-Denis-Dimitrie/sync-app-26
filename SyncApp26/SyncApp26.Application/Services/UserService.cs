using SyncApp26.Domain.IRepositories;
using SyncApp26.Domain.Entities;
using SyncApp26.Application.IServices;

namespace SyncApp26.Application.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;

        public UserService(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task<User?> GetUserByIdAsync(Guid id)
        {
            return await _userRepository.GetUserByIdAsync(id);
        }

        public async Task<IEnumerable<User>> GetAllUsersAsync()
        {
            return await _userRepository.GetAllUsersAsync();
        }

        public async Task<IEnumerable<User>> GetUsersByDepartmentIdAsync(Guid departmentId)
        {
            return await _userRepository.GetUsersByDepartmentIdAsync(departmentId);
        }

        public async Task<IEnumerable<User>> GetUsersAssignedToAsync(string assignedToPersonalId)
        {
            return await _userRepository.GetUsersAssignedToAsync(assignedToPersonalId);
        }

        public async Task AddUserAsync(User user)
        {
            await _userRepository.AddUserAsync(user);
        }

        public async Task UpdateUserAsync(User user)
        {
            await _userRepository.UpdateUserAsync(user);
        }

        public async Task DeleteUserAsync(Guid id)
        {
            await _userRepository.DeleteUserAsync(id);
        }

        public async Task<User?> GetUserByPersonalIdAsync(string personalId)
        {
            return await _userRepository.GetUserByPersonalIdAsync(personalId);
        }
    }
}