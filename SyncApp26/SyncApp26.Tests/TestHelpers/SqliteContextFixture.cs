using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SyncApp26.Domain.Entities;
using SyncApp26.Infrastructure.Context;

namespace SyncApp26.Tests.TestHelpers
{
    public sealed class SqliteContextFixture : IDisposable
    {
        private readonly SqliteConnection _connection;

        public ApplicationDbContext Context { get; }

        public SqliteContextFixture()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(_connection)
                .Options;

            Context = new ApplicationDbContext(options, NullLogger<ApplicationDbContext>.Instance);
            // EnsureCreated builds schema from the current model only - it never runs migration
            // .Sql() seed statements, so the Roles table starts empty here unlike a real database.
            Context.Database.EnsureCreated();
        }

        /// <summary>Finds or creates a Role row by name, so tests can grant roles without duplicating
        /// this lookup-or-insert logic in every fixture-based test class.</summary>
        public Role GetOrCreateRole(string name)
        {
            var existing = Context.Roles.FirstOrDefault(r => r.Name == name);
            if (existing != null) return existing;

            var role = new Role { Id = Guid.NewGuid(), Name = name, IsSystem = true, CreatedAt = DateTime.UtcNow };
            Context.Roles.Add(role);
            Context.SaveChanges();
            return role;
        }

        /// <summary>Grants a role to an already-persisted user.</summary>
        public void GrantRole(User user, string roleName)
        {
            var role = GetOrCreateRole(roleName);
            Context.UserRoleAssignments.Add(new UserRoleAssignment { UserId = user.Id, RoleId = role.Id });
            Context.SaveChanges();
        }

        public void Dispose()
        {
            Context.Dispose();
            _connection.Dispose();
        }
    }
}
