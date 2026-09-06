using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using UnityAgent.Core.Agent;
using UnityAgent.Core.Runtime;
using UnityAgent.Core.Workspace;

namespace UnityAgent.Host.Hosting;

internal sealed class AgentWorker : BackgroundService
{
	private readonly AgentSettings _settings;
	private readonly IAgentRuntime _agent;
	private readonly IWorkspaceProvider _workspaceProvider;

	public AgentWorker(IOptions<AgentSettings> settings, IAgentRuntime agent, IWorkspaceProvider workspaceProvider)
	{
		_settings = settings.Value;
		_agent = agent;
		_workspaceProvider = workspaceProvider;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var workspace = await _workspaceProvider.GetCurrentAsync(stoppingToken);

		Console.WriteLine($"UnityAgent | {_settings.Mode}");
		Console.WriteLine("Type a message. Stop the process to exit.");
		Console.WriteLine();

		while (!stoppingToken.IsCancellationRequested)
		{
			Console.Write("> ");

			var input = await Console.In.ReadLineAsync(stoppingToken);

			if (string.IsNullOrWhiteSpace(input))
				continue;

			var request = new AgentRequest(input, _settings.Mode, workspace);

			Console.WriteLine();

			await foreach (var agentEvent in _agent.RunAsync(request, stoppingToken))
			{
				if (agentEvent is AgentTextDelta text)
					Console.Write(text.Text);
			}

			Console.WriteLine();
			Console.WriteLine();
		}
	}
}
