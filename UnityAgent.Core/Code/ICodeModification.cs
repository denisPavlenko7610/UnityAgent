using UnityAgent.Core.Workspace;

namespace UnityAgent.Core.Code;

public interface ICodeModification
{
	Task<string> ApplyPatchAsync(
		ProjectWorkspace workspace, string patch, CancellationToken cancellationToken);
}
