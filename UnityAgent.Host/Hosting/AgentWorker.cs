using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UnityAgent.Core.Agent;
using UnityAgent.Core.Runtime;

namespace UnityAgent.Host.Hosting;

internal sealed class AgentWorker(IOptions<AgentSettings> settings, IAgentRuntime agent, ILogger<AgentWorker> logger)
	: BackgroundService
{
	private readonly AgentSettings _settings = settings.Value;

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		logger.LogInformation("UnityAgent started. Mode: {Mode}", _settings.Mode);

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
				var agentEvent in agent.RunAsync(request, stoppingToken))
			{
				if (agentEvent is AgentTextDelta text)
					Console.Write(text.Text);
			}

			Console.WriteLine();
			Console.WriteLine();
		}
	}
}
