using SyncApp26.Domain.Entities;

namespace SyncApp26.Domain.IRepositories
{
    public interface IDepartmentRepository
    {
        Task<Department?> GetDepartmentByIdAsync(Guid id);
        Task<IEnumerable<Department>> GetAllDepartmentsAsync();
        Task AddDepartmentAsync(Department department);
        Task UpdateDepartmentAsync(Department department);
        Task<IEnumerable<Department>> GetDeletedDepartmentsAsync();
        Task<Department?> GetDeletedDepartmentByIdAsync(Guid id);
    }
}