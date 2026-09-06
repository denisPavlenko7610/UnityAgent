using UnityAgent.Core.Workspace;

namespace UnityAgent.Core.Indexing;

public interface IProjectIndex
{
	Task EnsureReadyAsync(ProjectWorkspace workspace, CancellationToken cancellationToken);

	Task RefreshAsync(ProjectWorkspace workspace, CancellationToken cancellationToken);

	Task<IReadOnlyList<ProjectTextSearchResult>> SearchTextAsync(
		ProjectWorkspace workspace,
		string query,
		int limit,
		CancellationToken cancellationToken);
}
