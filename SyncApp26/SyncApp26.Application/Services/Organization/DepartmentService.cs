using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Application.IServices;

namespace SyncApp26.Application.Services
{
    public class DepartmentService : IDepartmentService
    {
        private readonly IDepartmentRepository _departmentRepository;

        public DepartmentService(IDepartmentRepository departmentRepository)
        {
            _departmentRepository = departmentRepository;
        }

        public async Task<Department?> GetDepartmentByIdAsync(Guid departmentId)
        {
            return await _departmentRepository.GetDepartmentByIdAsync(departmentId);
        }

        public async Task<IEnumerable<Department>> GetAllDepartmentsAsync()
        {
            return await _departmentRepository.GetAllDepartmentsAsync();
        }

        public async Task AddDepartmentAsync(Department department)
        {
            await _departmentRepository.AddDepartmentAsync(department);
        }

        public async Task UpdateDepartmentAsync(Department department)
        {
            await _departmentRepository.UpdateDepartmentAsync(department);
        }

        public async Task<IEnumerable<Department>> GetDeletedDepartmentsAsync()
        {
            return await _departmentRepository.GetDeletedDepartmentsAsync();
        }

        public async Task<Department?> GetDeletedDepartmentByIdAsync(Guid id)
        {
            return await _departmentRepository.GetDeletedDepartmentByIdAsync(id);
        }
    }
}