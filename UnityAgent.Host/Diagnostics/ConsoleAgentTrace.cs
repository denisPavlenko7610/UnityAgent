using UnityAgent.Core.Diagnostics;

namespace UnityAgent.Host.Diagnostics;

internal sealed class ConsoleAgentTrace : IAgentTrace
{
	public void ToolCompleted(string toolName, TimeSpan elapsed)
	{
		Console.WriteLine($"→ {toolName}  {elapsed.TotalMilliseconds:F0} ms");
	}
}
