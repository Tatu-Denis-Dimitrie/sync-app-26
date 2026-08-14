using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SyncApp26.Infrastructure.Context
{
    public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            
            // Design-time tools (dotnet ef) run with the startup project (SyncApp26.API) as the
            // working directory, matching Program.cs's ContentRootPath-relative resolution of
            // "DefaultConnection" - keep this literal in sync with that path.
            optionsBuilder.UseSqlite("Data Source=../SyncApp26.Infrastructure/SyncApp26.db");

            return new ApplicationDbContext(optionsBuilder.Options);
        }
    }
}
