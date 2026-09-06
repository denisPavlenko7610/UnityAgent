namespace UnityAgent.Core.Workspace;

public sealed record ProjectWorkspace(string RootPath, string Name)
{
	public string AssetsPath => Path.Combine(RootPath, "Assets");
	public string PackagesPath => Path.Combine(RootPath, "Packages");
	public string ProjectSettingsPath => Path.Combine(RootPath, "ProjectSettings");
}
