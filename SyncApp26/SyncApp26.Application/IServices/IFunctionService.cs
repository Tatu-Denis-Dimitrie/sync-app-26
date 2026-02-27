namespace SyncApp26.Application.IServices
{
    public interface IFunctionService
    {
        Task<IEnumerable<string>> GetAllFunctionNamesAsync();
        Task<IEnumerable<string>> GetFunctionByIdAsync(Guid functionId);
        Task<IEnumerable<string>> GetFunctionByDepartmentIdAsync(Guid departmentId);
        Task AddFunctionAsync(string functionName);
        Task DeleteFunctionAsync(Guid functionId);
    }
}