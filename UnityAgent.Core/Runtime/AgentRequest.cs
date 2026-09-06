using UnityAgent.Core.Agent;
using UnityAgent.Core.Workspace;

namespace UnityAgent.Core.Runtime;

public sealed record AgentRequest(
	string Message,
	AgentMode Mode,
	ProjectWorkspace Workspace,
	string? SessionId = null,
	IReadOnlyList<AgentPromptResource>? Resources = null);
