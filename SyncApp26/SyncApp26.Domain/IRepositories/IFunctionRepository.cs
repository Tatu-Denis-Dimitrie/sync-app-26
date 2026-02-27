namespace SyncApp26.Domain.IRepositories
{
    public interface IFunctionRepository
    {
        Task<IEnumerable<string>> GetAllFunctionNamesAsync();
        Task<IEnumerable<string>> GetFunctionByIdAsync(Guid functionId);
        Task AddFunctionAsync(string functionName);
        Task DeleteFunctionAsync(Guid functionId);
    }
}