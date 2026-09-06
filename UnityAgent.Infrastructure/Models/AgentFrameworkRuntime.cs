using System.ClientModel;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using UnityAgent.Core.Agent;
using UnityAgent.Core.Runtime;

namespace UnityAgent.Infrastructure.Models;

public sealed class AgentFrameworkRuntime : IAgentRuntime
{
	private readonly ChatClient _chatClient;
	private readonly ILoggerFactory _loggerFactory;

	public AgentFrameworkRuntime(IOptions<LocalModelSettings> settings, ILoggerFactory loggerFactory)
	{
		var model = settings.Value;

		_loggerFactory = loggerFactory;

		var clientOptions =
			new OpenAIClientOptions
			{
				Endpoint = new Uri(model.Endpoint)
			};

		_chatClient =
			new ChatClient(
				model: model.ModelId,
				credential: new ApiKeyCredential("lm-studio"),
				options: clientOptions);
	}

	public async IAsyncEnumerable<AgentRunEvent> RunAsync(
		AgentRequest request,
		[EnumeratorCancellation]
		CancellationToken cancellationToken)
	{
		AIAgent agent = _chatClient.AsAIAgent(instructions: AgentPolicy.Build(request.Mode),
				name: "UnityAgent",
				loggerFactory: _loggerFactory);

		await foreach (var update in agent.RunStreamingAsync(request.Message, cancellationToken: cancellationToken))
		{
			if (string.IsNullOrEmpty(update.Text))
				continue;

			yield return new AgentTextDelta(update.Text);
		}
	}
}
