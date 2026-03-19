using SyncApp26.Domain.Entities;

namespace SyncApp26.Application.IServices
{
    public interface IFunctionService
    {
        Task<IEnumerable<string>> GetAllFunctionNamesAsync();
        Task<IEnumerable<string>> GetFunctionByIdAsync(Guid functionId);
        Task<Function?> GetByNameAsync(string functionName);
        Task AddFunctionAsync(string functionName);
        Task DeleteFunctionAsync(Guid functionId);
    }
}