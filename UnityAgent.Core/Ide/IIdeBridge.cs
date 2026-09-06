using UnityAgent.Core.Workspace;

namespace UnityAgent.Core.Ide;

public interface IIdeBridge
{
	Task<IReadOnlyList<IdeToolInfo>> GetAvailableToolsAsync(CancellationToken cancellationToken);

	Task<string> CallToolAsync(
		ProjectWorkspace workspace,
		string toolName,
		IReadOnlyDictionary<string, object?>? arguments,
		CancellationToken cancellationToken);
}
