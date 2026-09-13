using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SyncApp26.Infrastructure.Context
{
    // synchronous is a per-connection SQLite setting (unlike journal_mode, it isn't persisted in
    // the db file), so it has to be re-applied on every physical connection the pool opens.
    public sealed class SqliteSynchronousInterceptor : DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA synchronous=NORMAL;";
            command.ExecuteNonQuery();
        }

        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            var command = connection.CreateCommand();
            await using var _ = command.ConfigureAwait(false);
            command.CommandText = "PRAGMA synchronous=NORMAL;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
