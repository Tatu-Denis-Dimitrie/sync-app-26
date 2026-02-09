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
        
        // Optimized method for CSV comparison - no tracking, minimal includes
        public async Task<List<User>> GetAllUsersForComparisonAsync()
        {
            return await _context.Users
                .AsNoTracking()
                .Include(u => u.Department)
                .Where(u => u.DeletedAt == null)
                .ToListAsync();
        }
        
        // Bulk update method for better performance
        public async Task UpdateUsersAsync(IEnumerable<User> users)
        {
            _context.Users.UpdateRange(users);
            await _context.SaveChangesAsync();
        }
        
        // Bulk add method
        public async Task AddUsersAsync(IEnumerable<User> users)
        {
            await _context.Users.AddRangeAsync(users);
            await _context.SaveChangesAsync();
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
    }
}