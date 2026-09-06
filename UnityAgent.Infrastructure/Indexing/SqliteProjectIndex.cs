using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using UnityAgent.Core.Indexing;
using UnityAgent.Core.Workspace;
using UnityAgent.Infrastructure.Storage;

namespace UnityAgent.Infrastructure.Indexing;

public sealed class SqliteProjectIndex : IProjectIndex
{
    private const long MaximumFileSize = 1024 * 1024;

    private static readonly HashSet<string> IndexedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".json",
        ".asmdef",
        ".asmref",
        ".shader",
        ".hlsl",
        ".compute",
        ".uxml",
        ".uss"
    };

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".idea",
        "bin",
        "obj",
        "Library",
        "Temp",
        "Logs"
    };

    private readonly ProjectDatabase _database;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshLocks;

    public SqliteProjectIndex(ProjectDatabase database)
    {
        _database = database;

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        _refreshLocks = new ConcurrentDictionary<string, SemaphoreSlim>(comparer);
    }

    public Task EnsureReadyAsync(ProjectWorkspace workspace, CancellationToken cancellationToken)
    {
        return _database.EnsureReadyAsync(workspace, cancellationToken);
    }

    public async Task RefreshAsync(ProjectWorkspace workspace, CancellationToken cancellationToken)
    {
        var key = Path.GetFullPath(workspace.RootPath);
        var refreshLock = _refreshLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        await refreshLock.WaitAsync(cancellationToken);

        try
        {
            await RefreshCoreAsync(workspace, cancellationToken);
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public async Task<IReadOnlyList<ProjectTextSearchResult>> SearchTextAsync(
        ProjectWorkspace workspace,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<ProjectTextSearchResult>();

        await RefreshAsync(workspace, cancellationToken);

        await using var connection = await _database.OpenAsync(workspace, cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                path,
                snippet(code_fts, 1, '[', ']', ' ... ', 24),
                bm25(code_fts)
            FROM code_fts
            WHERE code_fts MATCH $query
            ORDER BY bm25(code_fts)
            LIMIT $limit;
            """;

        command.Parameters.AddWithValue("$query", BuildFtsQuery(query));
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<ProjectTextSearchResult>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ProjectTextSearchResult(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetDouble(2)));
        }

        return results;
    }

    private async Task RefreshCoreAsync(
        ProjectWorkspace workspace,
        CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenAsync(workspace, cancellationToken);

        var existingFiles = await ReadExistingFilesAsync(connection, cancellationToken);

        using var transaction = connection.BeginTransaction();

        await using var deleteFts = CreateDeleteFtsCommand(connection, transaction);
        await using var insertFts = CreateInsertFtsCommand(connection, transaction);
        await using var upsertFile = CreateUpsertFileCommand(connection, transaction);
        await using var deleteFile = CreateDeleteFileCommand(connection, transaction);

        foreach (var file in EnumerateFiles(workspace.RootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(workspace.RootPath, file.FullName)
                .Replace('\\', '/');

            if (IsUnchanged(existingFiles, relativePath, file))
            {
                existingFiles.Remove(relativePath);
                continue;
            }

            if (file.Length > MaximumFileSize)
            {
                await DeleteIndexedFileAsync(
                    relativePath,
                    deleteFts,
                    deleteFile,
                    cancellationToken);

                existingFiles.Remove(relativePath);
                continue;
            }

            string content;

            try
            {
                content = await File.ReadAllTextAsync(file.FullName, cancellationToken);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            await UpsertIndexedFileAsync(
                relativePath,
                file,
                content,
                deleteFts,
                insertFts,
                upsertFile,
                cancellationToken);

            existingFiles.Remove(relativePath);
        }

        foreach (var deletedPath in existingFiles.Keys)
        {
            await DeleteIndexedFileAsync(
                deletedPath,
                deleteFts,
                deleteFile,
                cancellationToken);
        }

        transaction.Commit();
    }

    private static async Task<Dictionary<string, IndexedFileState>> ReadExistingFilesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT path, last_write_utc, length
            FROM files;
            """;

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var result = new Dictionary<string, IndexedFileState>(comparer);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetString(0)] = new IndexedFileState(
                reader.GetInt64(1),
                reader.GetInt64(2));
        }

        return result;
    }

    private static async Task UpsertIndexedFileAsync(
        string relativePath,
        FileInfo file,
        string content,
        SqliteCommand deleteFts,
        SqliteCommand insertFts,
        SqliteCommand upsertFile,
        CancellationToken cancellationToken)
    {
        deleteFts.Parameters["$path"].Value = relativePath;
        await deleteFts.ExecuteNonQueryAsync(cancellationToken);

        insertFts.Parameters["$path"].Value = relativePath;
        insertFts.Parameters["$content"].Value = content;
        await insertFts.ExecuteNonQueryAsync(cancellationToken);

        upsertFile.Parameters["$path"].Value = relativePath;
        upsertFile.Parameters["$lastWriteUtc"].Value = file.LastWriteTimeUtc.Ticks;
        upsertFile.Parameters["$length"].Value = file.Length;
        upsertFile.Parameters["$indexedUtc"].Value = DateTime.UtcNow.Ticks;

        await upsertFile.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteIndexedFileAsync(
        string relativePath,
        SqliteCommand deleteFts,
        SqliteCommand deleteFile,
        CancellationToken cancellationToken)
    {
        deleteFts.Parameters["$path"].Value = relativePath;
        await deleteFts.ExecuteNonQueryAsync(cancellationToken);

        deleteFile.Parameters["$path"].Value = relativePath;
        await deleteFile.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqliteCommand CreateDeleteFtsCommand(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = "DELETE FROM code_fts WHERE path = $path;";
        command.Parameters.Add("$path", SqliteType.Text);

        command.Prepare();

        return command;
    }

    private static SqliteCommand CreateInsertFtsCommand(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();

        command.Transaction = transaction;

        command.CommandText = """
            INSERT INTO code_fts(path, content)
            VALUES ($path, $content);
            """;

        command.Parameters.Add("$path", SqliteType.Text);
        command.Parameters.Add("$content", SqliteType.Text);

        command.Prepare();

        return command;
    }

    private static SqliteCommand CreateUpsertFileCommand(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();

        command.Transaction = transaction;

        command.CommandText = """
            INSERT INTO files(path, last_write_utc, length, indexed_utc)
            VALUES ($path, $lastWriteUtc, $length, $indexedUtc)
            ON CONFLICT(path) DO UPDATE SET
                last_write_utc = excluded.last_write_utc,
                length = excluded.length,
                indexed_utc = excluded.indexed_utc;
            """;

        command.Parameters.Add("$path", SqliteType.Text);
        command.Parameters.Add("$lastWriteUtc", SqliteType.Integer);
        command.Parameters.Add("$length", SqliteType.Integer);
        command.Parameters.Add("$indexedUtc", SqliteType.Integer);

        command.Prepare();

        return command;
    }

    private static SqliteCommand CreateDeleteFileCommand(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = "DELETE FROM files WHERE path = $path;";
        command.Parameters.Add("$path", SqliteType.Text);

        command.Prepare();

        return command;
    }

    private static bool IsUnchanged(
        IReadOnlyDictionary<string, IndexedFileState> existingFiles,
        string relativePath,
        FileInfo file)
    {
        return existingFiles.TryGetValue(relativePath, out var existing) &&
               existing.LastWriteUtc == file.LastWriteTimeUtc.Ticks &&
               existing.Length == file.Length;
    }

    private static IEnumerable<FileInfo> EnumerateFiles(string rootPath)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(rootPath));

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            DirectoryInfo[] directories;
            FileInfo[] files;

            try
            {
                directories = directory.GetDirectories();
                files = directory.GetFiles();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var child in directories)
            {
                if (!ExcludedDirectories.Contains(child.Name))
                    pending.Push(child);
            }

            foreach (var file in files)
            {
                if (IndexedExtensions.Contains(file.Extension))
                    yield return file;
            }
        }
    }

    private static string BuildFtsQuery(string query)
    {
        var terms = query.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(" AND ", terms.Select(EscapeFtsTerm));
    }

    private static string EscapeFtsTerm(string term)
    {
        var escaped = term.Replace("\"", "\"\"");

        return $"\"{escaped}\"*";
    }

    private sealed record IndexedFileState(long LastWriteUtc, long Length);
}
