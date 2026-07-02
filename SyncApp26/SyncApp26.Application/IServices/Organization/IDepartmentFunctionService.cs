namespace SyncApp26.Application.IServices
{
    public interface IDepartmentFunctionService
    {
        Task<IEnumerable<string>> GetFunctionsByDepartmentIdAsync(Guid departmentId);
        Task AddFunctionToDepartmentAsync(Guid departmentId, string functionName);
        Task RemoveFunctionFromDepartmentAsync(Guid departmentId, string functionName);
    }
}