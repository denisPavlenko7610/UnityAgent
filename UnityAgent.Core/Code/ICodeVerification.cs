using UnityAgent.Core.Workspace;

namespace UnityAgent.Core.Code;

public interface ICodeVerification
{
	Task<string> BuildAsync(ProjectWorkspace workspace, CancellationToken cancellationToken);
}
