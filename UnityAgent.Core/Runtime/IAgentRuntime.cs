namespace UnityAgent.Core.Runtime;

public interface IAgentRuntime
{
	IAsyncEnumerable<AgentRunEvent> RunAsync(AgentRequest request, CancellationToken cancellationToken);
}
