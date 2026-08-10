using SyncApp26.Domain.IRepositories;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
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
                .Include(u => u.Function)
                .Include(u => u.AssignedTo)
                .Include(u => u.InitialTrainings)
                .Include(u => u.RoleAssignments).ThenInclude(a => a.Role)
                .Where(u => u.DeletedAt == null)
                .FirstOrDefaultAsync(u => u.Id == id);
        }

        public async Task<IEnumerable<User>> GetAllUsersAsync()
        {
            return await _context.Users
                .Include(u => u.Department)
                .Include(u => u.Function)
                .Include(u => u.AssignedTo)
                .Include(u => u.InitialTrainings)
                .Include(u => u.RoleAssignments).ThenInclude(a => a.Role)
                .Where(u => u.DeletedAt == null)
                .WithoutRole(Roles.Admin)
                .ToListAsync();
        }

        // Optimized method for CSV comparison - no tracking, minimal includes. Role assignments are
        // deliberately NOT loaded here: nothing on the comparison path reads a user's role, and this
        // query runs over the entire roster (up to ~250k users per deployment) on every CSV upload.
        public async Task<List<User>> GetAllUsersForComparisonAsync()
        {
            return await _context.Users
                .AsNoTracking()
                .Include(u => u.Department)
                .Include(u => u.Function)
                .Include(u => u.AssignedTo)
                .Where(u => u.DeletedAt == null)
                .WithoutRole(Roles.Admin)
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
                .Include(u => u.Function)
                .Include(u => u.AssignedTo)
                .Include(u => u.RoleAssignments).ThenInclude(a => a.Role)
                .Where(u => u.DepartmentId == departmentId && u.DeletedAt == null)
                .WithoutRole(Roles.Admin)
                .ToListAsync();
        }

        public async Task<IEnumerable<User>> GetUsersAssignedToAsync(Guid assignedToId)
        {
            return await _context.Users
                .Include(u => u.Department)
                .Include(u => u.Function)
                .Include(u => u.AssignedTo)
                .Include(u => u.RoleAssignments).ThenInclude(a => a.Role)
                .Where(u => u.AssignedToId == assignedToId && u.DeletedAt == null)
                .WithoutRole(Roles.Admin)
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
            return await _context.Users
                .Where(u => u.AssignedToId == userId && u.DeletedAt == null)
                .WithoutRole(Roles.Admin)
                .AnyAsync();
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await _context.Users
                .Include(u => u.Department)
                .Include(u => u.Function)
                .Include(u => u.AssignedTo)
                .Include(u => u.RoleAssignments).ThenInclude(a => a.Role)
                .Where(u => u.DeletedAt == null)
                .FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<User?> GetUserByPersonalIdAsync(string personalId)
        {
            return await _context.Users
                .Include(u => u.Department)
                .Include(u => u.Function)
                .Include(u => u.AssignedTo)
                .Include(u => u.RoleAssignments).ThenInclude(a => a.Role)
                .Where(u => u.DeletedAt == null)
                .WithoutRole(Roles.Admin)
                .FirstOrDefaultAsync(u => u.PersonalId == personalId);
        }

        public async Task<(List<User> Items, int TotalCount)> SearchUsersAsync(string? search, int page, int pageSize)
        {
            var query = _context.Users
                .Include(u => u.Department)
                .Where(u => u.DeletedAt == null)
                .WithoutRole(Roles.Admin);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(u =>
                    EF.Functions.Like(u.FirstName, $"%{term}%")
                    || EF.Functions.Like(u.LastName, $"%{term}%")
                    || EF.Functions.Like(u.Email, $"%{term}%"));
            }

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        public async Task<Role?> GetRoleByNameAsync(string roleName)
        {
            return await _context.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
        }

        public async Task<IEnumerable<User>> GetUsersInRoleAsync(string roleName)
        {
            // Deliberately no "exclude Admin" filter here, unlike GetAllUsersAsync — a caller asking
            // "who holds role X" wants every holder, including admins (e.g. the Admin role itself).
            return await _context.Users
                .Include(u => u.Department)
                .Include(u => u.Function)
                .Include(u => u.AssignedTo)
                .Include(u => u.RoleAssignments).ThenInclude(a => a.Role)
                .Where(u => u.DeletedAt == null)
                .WithRole(roleName)
                .ToListAsync();
        }

        public async Task<bool> IsInRoleAsync(Guid userId, string roleName)
        {
            return await _context.Users
                .Where(u => u.Id == userId)
                .WithRole(roleName)
                .AnyAsync();
        }

        public async Task<List<Role>> GetAllRolesAsync()
        {
            return await _context.Roles.OrderBy(r => r.Name).ToListAsync();
        }

        public async Task<Role?> GetRoleByIdAsync(Guid id)
        {
            return await _context.Roles.FirstOrDefaultAsync(r => r.Id == id);
        }

        public async Task AddRoleAsync(Role role)
        {
            await _context.Roles.AddAsync(role);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteRoleAsync(Role role)
        {
            _context.Roles.Remove(role);
            await _context.SaveChangesAsync();
        }

        public async Task<bool> RoleHasAssignmentsAsync(Guid roleId)
        {
            return await _context.UserRoleAssignments.AnyAsync(a => a.RoleId == roleId);
        }
    }
}
