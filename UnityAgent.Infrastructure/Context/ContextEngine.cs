using System.Text;
using UnityAgent.Core.Context;
using UnityAgent.Core.Memory;
using UnityAgent.Core.Runtime;

namespace UnityAgent.Infrastructure.Context;

public sealed class ContextEngine : IContextEngine
{
	private readonly ISessionMemory _sessionMemory;

	public ContextEngine(ISessionMemory sessionMemory)
	{
		_sessionMemory = sessionMemory;
	}

	public Task<AgentContext> BuildAsync(
		AgentRequest request,
		CancellationToken cancellationToken)
	{
		var builder = new StringBuilder();
		var history = _sessionMemory.GetContext(request.SessionId);

		if (!string.IsNullOrWhiteSpace(history))
		{
			builder.AppendLine("Recent conversation:");
			builder.AppendLine(history);
			builder.AppendLine();
		}

		builder.AppendLine("Current user request:");
		builder.AppendLine(request.Message);

		return Task.FromResult(new AgentContext(builder.ToString()));
	}
}
