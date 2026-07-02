namespace SyncApp26.Domain.IRepositories
{
    public interface IFunctionRepository
    {
        Task<IEnumerable<string>> GetAllFunctionNamesAsync();
        Task<IEnumerable<string>> GetFunctionByIdAsync(Guid functionId);
        Task<SyncApp26.Domain.Entities.Function?> GetByNameAsync(string functionName);
        Task AddFunctionAsync(string functionName);
        Task DeleteFunctionAsync(Guid functionId);
    }
}