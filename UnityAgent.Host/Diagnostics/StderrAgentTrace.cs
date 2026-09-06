using UnityAgent.Core.Diagnostics;

namespace UnityAgent.Host.Diagnostics;

internal sealed class StderrAgentTrace : IAgentTrace
{
	public void ToolCompleted(string toolName, TimeSpan elapsed)
	{
		Console.Error.WriteLine($"{toolName} {elapsed.TotalMilliseconds:F0} ms");
	}
}
