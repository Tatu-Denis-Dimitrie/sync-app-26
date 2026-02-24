using Microsoft.EntityFrameworkCore;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Infrastructure.Context;

namespace SyncApp26.Infrastructure.Repositories
{
    public class DepartmentRepository : IDepartmentRepository
    {
        private readonly ApplicationDbContext _context;

        public DepartmentRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Department?> GetDepartmentByIdAsync(Guid id)
        {
            return await _context.Departments
                .Where(d => d.DeletedAt == null)
                .FirstOrDefaultAsync(d => d.Id == id);
        }

        public async Task<IEnumerable<Department>> GetAllDepartmentsAsync()
        {
            return await _context.Departments
                .Where(d => d.DeletedAt == null)
                .ToListAsync();
        }

        public async Task AddDepartmentAsync(Department department)
        {
            await _context.Departments.AddAsync(department);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateDepartmentAsync(Department department)
        {
            _context.Departments.Update(department);
            await _context.SaveChangesAsync();
        }

        public async Task<IEnumerable<Department>> GetDeletedDepartmentsAsync()
        {
            return await _context.Departments
                .Where(d => d.DeletedAt != null)
                .ToListAsync();
        }

        public async Task<Department?> GetDeletedDepartmentByIdAsync(Guid id)
        {
            return await _context.Departments
                .Where(d => d.DeletedAt != null)
                .FirstOrDefaultAsync(d => d.Id == id);
        }
    }
}