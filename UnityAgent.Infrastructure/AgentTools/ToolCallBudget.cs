namespace UnityAgent.Infrastructure.AgentTools;

internal sealed class ToolCallBudget
{
	private readonly int _maximumSteps;
	private readonly HashSet<string> _executedCalls = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _gate = new();

	private int _steps;

	public ToolCallBudget(int maximumSteps)
	{
		_maximumSteps = maximumSteps;
	}

	public string? TryBegin(string key)
	{
		lock (_gate)
		{
			if (_executedCalls.Contains(key))
				return "Duplicate tool call blocked. Reuse the previous result.";

			if (_steps >= _maximumSteps)
				return "Tool budget exhausted. Answer using the evidence already collected.";

			_executedCalls.Add(key);
			_steps++;

			return null;
		}
	}
}
