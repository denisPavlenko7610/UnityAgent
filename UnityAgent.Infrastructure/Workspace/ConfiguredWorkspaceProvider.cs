using Microsoft.Extensions.Options;
using UnityAgent.Core.Workspace;

namespace UnityAgent.Infrastructure.Workspace;

public sealed class ConfiguredWorkspaceProvider : IWorkspaceProvider
{
    private readonly ProjectWorkspace _workspace;

    public ConfiguredWorkspaceProvider(IOptions<WorkspaceSettings> options)
    {
        var configuredPath = options.Value.ProjectPath;

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException("Workspace project path is not configured.");
        }

        var rootPath = Path.GetFullPath(configuredPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        ValidateUnityProject(rootPath);

        var name = new DirectoryInfo(rootPath).Name;

        _workspace = new ProjectWorkspace(rootPath, name);
    }

    public ValueTask<ProjectWorkspace> GetCurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(_workspace);
    }

    private static void ValidateUnityProject(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Workspace does not exist: {rootPath}");
        }

        var requiredDirectories =
			new[]
            {
                "Assets",
                "Packages",
                "ProjectSettings"
            };

        foreach (var directory in requiredDirectories)
        {
            var path = Path.Combine(rootPath, directory);

            if (!Directory.Exists(path))
            {
                throw new InvalidOperationException(
                    $"'{rootPath}' is not a valid Unity project. " +
                    $"Missing directory: {directory}");
            }
        }
    }
}
