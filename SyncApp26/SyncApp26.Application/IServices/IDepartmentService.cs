using SyncApp26.Domain.Entities;

namespace SyncApp26.Application.IServices
{
    public interface IDepartmentService
    {
        Task<Department?> GetDepartmentByIdAsync(Guid departmentId);
        Task<IEnumerable<Department>> GetAllDepartmentsAsync();
        Task AddDepartmentAsync(Department department);
        Task UpdateDepartmentAsync(Department department);
        Task<IEnumerable<Department>> GetDeletedDepartmentsAsync();
        Task<Department?> GetDeletedDepartmentByIdAsync(Guid id);
    }
}