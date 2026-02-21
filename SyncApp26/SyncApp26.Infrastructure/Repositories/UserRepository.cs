using SyncApp26.Domain.IRepositories;
using SyncApp26.Domain.Entities;
using SyncApp26.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace SyncApp26.Infrastructure.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly ApplicationDbContext _context;

        public UserRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<User?> GetUserByIdAsync(Guid id)
        {
            return await _context.Users
                .Include(u => u.Department)
                .Include(u => u.AssignedTo)
                .Where(u => u.DeletedAt == null)
                .FirstOrDefaultAsync(u => u.Id == id);
        }

        public async Task<IEnumerable<User>> GetAllUsersAsync()
        {
            return await _context.Users
                .Include(u => u.Department)
                .Include(u => u.AssignedTo)
                .Where(u => u.DeletedAt == null)
                .ToListAsync();
        }

        public async Task<IEnumerable<User>> GetUsersByDepartmentIdAsync(Guid departmentId)
        {
            return await _context.Users
                .Include(u => u.Department)
                .Include(u => u.AssignedTo)
                .Where(u => u.DepartmentId == departmentId && u.DeletedAt == null)
                .ToListAsync();
        }

        public async Task<IEnumerable<User>> GetUsersAssignedToAsync(Guid assignedToId)
        {
            return await _context.Users
                .Include(u => u.Department)
                .Include(u => u.AssignedTo)
                .Where(u => u.AssignedToId == assignedToId && u.DeletedAt == null)
                .ToListAsync();
        }

        public async Task AddUserAsync(User user)
        {
            await _context.Users.AddAsync(user);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateUserAsync(User user)
        {
            _context.Users.Update(user);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteUserAsync(Guid id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
                user.DeletedAt = DateTime.UtcNow;
                _context.Users.Update(user);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<bool> IsUserLineManagerAsync(Guid userId)
        {
            return await _context.Users.AnyAsync(u => u.AssignedToId == userId && u.DeletedAt == null);
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await _context.Users
                .Include(u => u.Department)
                .Include(u => u.AssignedTo)
                .Where(u => u.DeletedAt == null)
                .FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<User?> GetUserByPersonalIdAsync(string personalId)
        {
            return await _context.Users
                .Include(u => u.Department)
                .Include(u => u.AssignedTo)
                .Where(u => u.DeletedAt == null)
                .FirstOrDefaultAsync(u => u.PersonalId == personalId);
        }
    }
}