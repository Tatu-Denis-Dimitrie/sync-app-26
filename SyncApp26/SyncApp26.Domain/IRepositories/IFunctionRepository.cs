namespace SyncApp26.Domain.IRepositories
{
    public interface IFunctionRepository
    {
        Task<IEnumerable<string>> GetAllFunctionNamesAsync();
        Task<IEnumerable<string>> GetFunctionByIdAsync(Guid functionId);
        Task<IEnumerable<string>> GetFunctionByDepartmentIdAsync(Guid departmentId);
        Task AddFunctionAsync(string functionName);
        Task DeleteFunctionAsync(Guid functionId);
    }
}