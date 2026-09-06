namespace UnityAgent.Core.Workspace;

public interface IWorkspaceProvider
{
	ValueTask<ProjectWorkspace> GetCurrentAsync(CancellationToken cancellationToken);
}
