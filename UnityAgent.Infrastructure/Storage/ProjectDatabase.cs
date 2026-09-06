using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using UnityAgent.Core.Workspace;

namespace UnityAgent.Infrastructure.Storage;

public sealed class ProjectDatabase
{
    private readonly ProjectStorage _storage;

    private readonly ConcurrentDictionary<string, Task> _initializations = new();

    public ProjectDatabase(ProjectStorage storage)
    {
        _storage = storage;
    }

    public async Task EnsureReadyAsync(ProjectWorkspace workspace, CancellationToken cancellationToken)
    {
        var paths = _storage.GetPaths(workspace);

        var initialization = _initializations.GetOrAdd(
            paths.ProjectId,
            _ => InitializeAsync(paths));

        try
        {
            await initialization.WaitAsync(cancellationToken);
        }
        catch
        {
            if (initialization.IsFaulted)
                _initializations.TryRemove(paths.ProjectId, out _);

            throw;
        }
    }

    public async Task<SqliteConnection> OpenAsync(
        ProjectWorkspace workspace, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(workspace, cancellationToken);

        var paths = _storage.GetPaths(workspace);
        var connection = CreateConnection(paths.DatabasePath);

        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    private static async Task InitializeAsync(ProjectStoragePaths paths)
    {
        Directory.CreateDirectory(paths.DirectoryPath);

        await using var connection = CreateConnection(paths.DatabasePath);

        await connection.OpenAsync();

        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;");
        await ExecuteAsync(connection, "PRAGMA synchronous=NORMAL;");

        const string schema = """
            CREATE TABLE IF NOT EXISTS files
            (
                path            TEXT PRIMARY KEY,
                last_write_utc  INTEGER NOT NULL,
                length          INTEGER NOT NULL,
                indexed_utc     INTEGER NOT NULL
            );

            CREATE VIRTUAL TABLE IF NOT EXISTS code_fts
            USING fts5
            (
                path UNINDEXED,
                content,
                tokenize = 'unicode61'
            );

            CREATE TABLE IF NOT EXISTS retrieval_cache
            (
                cache_key       TEXT PRIMARY KEY,
                value           TEXT NOT NULL,
                created_utc     INTEGER NOT NULL,
                last_access_utc INTEGER NOT NULL
            );

            PRAGMA user_version = 1;
            """;

        await ExecuteAsync(connection, schema);
    }

    private static SqliteConnection CreateConnection(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true
        };

        return new SqliteConnection(connectionString.ToString());
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        await command.ExecuteNonQueryAsync();
    }
}
