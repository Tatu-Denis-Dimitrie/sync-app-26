using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;

namespace SyncApp26.Application.Services
{
    public class FunctionService : IFunctionService
    {
        private readonly IFunctionRepository _functionRepository;

        public FunctionService(IFunctionRepository functionRepository)
        {
            _functionRepository = functionRepository;
        }

        public Task AddFunctionAsync(string functionName)
        {
            return _functionRepository.AddFunctionAsync(functionName);
        }

        public Task DeleteFunctionAsync(Guid functionId)
        {
            return _functionRepository.DeleteFunctionAsync(functionId);
        }

        public Task<IEnumerable<string>> GetAllFunctionNamesAsync()
        {
            return _functionRepository.GetAllFunctionNamesAsync();
        }

        public Task<IEnumerable<string>> GetFunctionByIdAsync(Guid functionId)
        {
            return _functionRepository.GetFunctionByIdAsync(functionId);
        }

        public Task<Function?> GetByNameAsync(string functionName)
        {
            return _functionRepository.GetByNameAsync(functionName);
        }
    }
}