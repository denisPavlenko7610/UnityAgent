using System.Security.Cryptography;
using System.Text;
using UnityAgent.Core.Workspace;

namespace UnityAgent.Infrastructure.Storage;

public sealed class ProjectStorage
{
	private readonly string _projectsRoot;

	public string ProjectsRoot => _projectsRoot;

	public ProjectStorage()
	{
		var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

		_projectsRoot = Path.Combine(localAppData, "UnityAgent", "Projects");
	}

	public ProjectStoragePaths GetPaths(ProjectWorkspace workspace)
	{
		var projectId = CreateProjectId(workspace.RootPath);
		var directoryPath = Path.Combine(_projectsRoot, projectId);
		var databasePath = Path.Combine(directoryPath, "index.db");

		return new ProjectStoragePaths(projectId, directoryPath, databasePath);
	}

	private static string CreateProjectId(string rootPath)
	{
		var normalizedPath = Path.GetFullPath(rootPath)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		if (OperatingSystem.IsWindows())
			normalizedPath = normalizedPath.ToUpperInvariant();

		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));

		return Convert.ToHexString(hash)[..16].ToLowerInvariant();
	}
}
