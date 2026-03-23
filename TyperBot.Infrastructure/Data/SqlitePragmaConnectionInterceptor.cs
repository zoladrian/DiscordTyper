using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TyperBot.Infrastructure.Data;

/// <summary>
/// Applies SQLite pragmas on every opened connection: WAL (fewer writer/readers blocks),
/// NORMAL synchronous (reasonable durability on VPS), busy timeout (wait on lock instead of failing).
/// </summary>
public sealed class SqlitePragmaConnectionInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyPragmas(connection);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (connection is not SqliteConnection sqlite)
            return;

        await using var cmd = sqlite.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        _ = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        cmd.CommandText = "PRAGMA synchronous=NORMAL;";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        cmd.CommandText = "PRAGMA busy_timeout=5000;";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyPragmas(DbConnection connection)
    {
        if (connection is not SqliteConnection sqlite)
            return;

        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        cmd.ExecuteScalar();
        cmd.CommandText = "PRAGMA synchronous=NORMAL;";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
    }
}
