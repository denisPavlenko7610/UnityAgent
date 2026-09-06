using UnityAgent.Core.Runtime;

namespace UnityAgent.Core.Context;

public interface IContextEngine
{
	Task<AgentContext> BuildAsync(AgentRequest request, CancellationToken cancellationToken);
}
