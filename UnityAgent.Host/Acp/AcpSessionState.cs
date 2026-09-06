using UnityAgent.Core.Agent;
using UnityAgent.Core.Workspace;

namespace UnityAgent.Host.Acp;

internal sealed class AcpSessionState
{
	public AcpSessionState(ProjectWorkspace workspace, AgentMode mode)
	{
		Workspace = workspace;
		Mode = mode;
	}

	public ProjectWorkspace Workspace { get; }

	public AgentMode Mode { get; set; }
}
