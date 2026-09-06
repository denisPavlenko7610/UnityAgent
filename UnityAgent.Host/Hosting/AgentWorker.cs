using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UnityAgent.Core.Agent;
using UnityAgent.Core.Ide;
using UnityAgent.Core.Runtime;
using UnityAgent.Core.Workspace;

namespace UnityAgent.Host.Hosting;

internal sealed class AgentWorker
	: BackgroundService
{
	private readonly AgentSettings _settings;
	private readonly IWorkspaceProvider _workspaceProvider;
	private readonly IAgentRuntime _agent;
	private readonly ILogger<AgentWorker> _logger;
	private readonly IIdeBridge _ide;

	public AgentWorker(
		IOptions<AgentSettings> settings,
		IAgentRuntime agent,
		IWorkspaceProvider workspaceProvider,
		ILogger<AgentWorker> logger,
		IIdeBridge ide
	)
	{
		_settings = settings.Value;
		_agent = agent;
		_workspaceProvider = workspaceProvider;
		_logger = logger;
		_ide = ide;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var ideTools = await _ide.GetAvailableToolsAsync(stoppingToken);

		_logger.LogInformation("Rider MCP connected. Available IDE tools: {ToolCount}", ideTools.Count);

		var workspace = await _workspaceProvider.GetCurrentAsync(stoppingToken);

		_logger.LogInformation("Workspace: {WorkspaceName} ({WorkspacePath})", workspace.Name, workspace.RootPath);
		_logger.LogInformation("UnityAgent started. Mode: {Mode}", _settings.Mode);

		Console.WriteLine();
		Console.WriteLine("UnityAgent");
		Console.WriteLine("Type a message. Stop the process to exit.");
		Console.WriteLine();

		while (!stoppingToken.IsCancellationRequested)
		{
			Console.Write("> ");

			var input = await Console.In.ReadLineAsync(stoppingToken);

			if (string.IsNullOrWhiteSpace(input))
				continue;

			var request = new AgentRequest(input, _settings.Mode);

			Console.WriteLine();

			await foreach (
				var agentEvent in _agent.RunAsync(request, stoppingToken))
			{
				if (agentEvent is AgentTextDelta text)
					Console.Write(text.Text);
			}

			Console.WriteLine();
			Console.WriteLine();
		}
	}
}
