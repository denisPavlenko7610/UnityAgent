namespace UnityAgent.Core.Diagnostics;

public interface IAgentTrace
{
	void ToolCompleted(string toolName, TimeSpan elapsed);
}
