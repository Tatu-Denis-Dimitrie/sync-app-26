namespace SyncApp26.Domain.IRepositories
{
    public interface IDepartmentFunctionRepository
    {
        Task<IEnumerable<string>> GetFunctionsByDepartmentIdAsync(Guid departmentId);
        Task AddFunctionToDepartmentAsync(Guid departmentId, string functionName);
        Task RemoveFunctionFromDepartmentAsync(Guid departmentId, string functionName);
    }
}