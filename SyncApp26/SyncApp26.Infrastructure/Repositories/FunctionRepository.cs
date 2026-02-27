using SyncApp26.Domain.IRepositories;
using SyncApp26.Domain.Entities;
using SyncApp26.Infrastructure.Context;

namespace SyncApp26.Infrastructure.Repositories
{
    public class FunctionRepository : IFunctionRepository
    {
        private readonly ApplicationDbContext _context;

        public FunctionRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public Task AddFunctionAsync(string functionName)
        {
            var function = new Function
            {
                Id = Guid.NewGuid(),
                Name = functionName
            };

            _context.Functions.Add(function);
            _context.SaveChanges();
            return Task.CompletedTask;
        }

        public Task DeleteFunctionAsync(Guid functionId)
        {
            var function = _context.Functions.Find(functionId);
            if (function != null)
            {
                function.DeletedAt = DateTime.UtcNow;
                _context.SaveChanges();
            }
            return Task.CompletedTask;
        }

        public Task<IEnumerable<string>> GetAllFunctionNamesAsync()
        {
            var functionNames = _context.Functions
                .Where(f => f.DeletedAt == null)
                .Select(f => f.Name)
                .ToList();

            return Task.FromResult(functionNames.AsEnumerable());
        }

        public Task<IEnumerable<string>> GetFunctionByDepartmentIdAsync(Guid departmentId)
        {
            var functionNames = _context.DepartmentFunctions
                .Where(df => df.DepartmentId == departmentId && df.Function.DeletedAt == null)
                .Select(df => df.Function.Name)
                .ToList();

            return Task.FromResult(functionNames.AsEnumerable());
        }

        public Task<IEnumerable<string>> GetFunctionByIdAsync(Guid functionId)
        {
            var functionNames = _context.Functions
                .Where(f => f.Id == functionId && f.DeletedAt == null)
                .Select(f => f.Name)
                .ToList();

            return Task.FromResult(functionNames.AsEnumerable());
        }
    }
}