namespace UnityAgent.Infrastructure.Storage;

public sealed record ProjectStoragePaths(
	string ProjectId,
	string DirectoryPath,
	string DatabasePath);
