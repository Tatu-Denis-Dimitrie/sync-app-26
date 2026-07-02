using SyncApp26.Application.IServices;
using SyncApp26.Domain.IRepositories;

namespace SyncApp26.Application.Services
{
    public class DepartmentFunctionService : IDepartmentFunctionService
    {
        private readonly IDepartmentFunctionRepository _departmentFunctionRepository;

        public DepartmentFunctionService(IDepartmentFunctionRepository departmentFunctionRepository)
        {
            _departmentFunctionRepository = departmentFunctionRepository;
        }

        public Task<IEnumerable<string>> GetFunctionsByDepartmentIdAsync(Guid departmentId)
        {
            return _departmentFunctionRepository.GetFunctionsByDepartmentIdAsync(departmentId);
        }

        public Task AddFunctionToDepartmentAsync(Guid departmentId, string functionName)
        {
            return _departmentFunctionRepository.AddFunctionToDepartmentAsync(departmentId, functionName);
        }

        public Task RemoveFunctionFromDepartmentAsync(Guid departmentId, string functionName)
        {
            return _departmentFunctionRepository.RemoveFunctionFromDepartmentAsync(departmentId, functionName);
        }
    }
}