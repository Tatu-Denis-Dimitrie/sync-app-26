using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

            Context = new ApplicationDbContext(options);
            Context.Database.EnsureCreated();
        }

        public void Dispose()
        {
            Context.Dispose();
            _connection.Dispose();
        }
    }
}
