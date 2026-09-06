using UnityAgent.Core.Runtime;

namespace UnityAgent.Host.Acp;

internal sealed record AcpPrompt(string Text, IReadOnlyList<AgentPromptResource> Resources);
