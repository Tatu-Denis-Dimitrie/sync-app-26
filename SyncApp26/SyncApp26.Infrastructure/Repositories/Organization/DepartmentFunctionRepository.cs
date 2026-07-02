using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Infrastructure.Context;

namespace SyncApp26.Infrastructure.Repositories
{
    public class DepartmentFunctionRepository : IDepartmentFunctionRepository
    {
        private readonly ApplicationDbContext _context;

        public DepartmentFunctionRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public Task<IEnumerable<string>> GetFunctionsByDepartmentIdAsync(Guid departmentId)
        {
            var functionNames = _context.DepartmentFunctions
                .Where(df => df.DepartmentId == departmentId && df.Function.DeletedAt == null)
                .Select(df => df.Function.Name)
                .ToList();

            return Task.FromResult(functionNames.AsEnumerable());
        }

        public Task AddFunctionToDepartmentAsync(Guid departmentId, string functionName)
        {
            var function = _context.Functions.FirstOrDefault(f => f.Name == functionName && f.DeletedAt == null);
            if (function != null)
            {
                var departmentFunction = new DepartmentFunction
                {
                    DepartmentId = departmentId,
                    FunctionId = function.Id
                };

                _context.DepartmentFunctions.Add(departmentFunction);
                _context.SaveChanges();
            }
            return Task.CompletedTask;
        }

        public Task RemoveFunctionFromDepartmentAsync(Guid departmentId, string functionName)
        {
            var function = _context.Functions.FirstOrDefault(f => f.Name == functionName && f.DeletedAt == null);
            if (function != null)
            {
                var departmentFunction = _context.DepartmentFunctions.FirstOrDefault(df => df.DepartmentId == departmentId && df.FunctionId == function.Id);
                if (departmentFunction != null)
                {
                    _context.DepartmentFunctions.Remove(departmentFunction);
                    _context.SaveChanges();
                }
            }
            return Task.CompletedTask;
        }
    }
}