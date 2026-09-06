using System.ClientModel;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using UnityAgent.Core.Agent;
using UnityAgent.Core.Code;
using UnityAgent.Core.Context;
using UnityAgent.Core.Diagnostics;
using UnityAgent.Core.Indexing;
using UnityAgent.Core.Memory;
using UnityAgent.Core.Runtime;
using UnityAgent.Core.Workspace;
using UnityAgent.Infrastructure.AgentTools;

namespace UnityAgent.Infrastructure.Models;

public sealed class AgentFrameworkRuntime : IAgentRuntime
{
    private readonly LocalModelSettings _settings;
    private readonly AgentSettings _agentSettings;
    private readonly LmStudioModelResolver _modelResolver;
    private readonly ICodeIntelligence _code;
    private readonly IContextEngine _contextEngine;
    private readonly IAgentTrace _trace;
    private readonly ILoggerFactory _loggerFactory;
	private readonly ISessionMemory _sessionMemory;
	private readonly IProjectIndex _projectIndex;

    private readonly ConcurrentDictionary<string, ChatClient> _clients = new();

    public AgentFrameworkRuntime(
        IOptions<LocalModelSettings> settings,
        IOptions<AgentSettings> agentSettings,
        LmStudioModelResolver modelResolver,
        ICodeIntelligence code,
        IContextEngine contextEngine,
        IAgentTrace trace,
        ILoggerFactory loggerFactory,
		ISessionMemory sessionMemory,
		IProjectIndex projectIndex
	)
    {
        _settings = settings.Value;
        _agentSettings = agentSettings.Value;
        _modelResolver = modelResolver;
        _code = code;
        _contextEngine = contextEngine;
        _trace = trace;
        _loggerFactory = loggerFactory;
		_sessionMemory = sessionMemory;
		_projectIndex = projectIndex;
	}

	public async IAsyncEnumerable<AgentRunEvent> RunAsync(
		AgentRequest request,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var context = await _contextEngine.BuildAsync(request, cancellationToken);
		var modelId = await _modelResolver.GetLoadedModelIdAsync(cancellationToken);
		var chatClient = _clients.GetOrAdd(modelId, CreateChatClient);
		var tools = CreateTools(request);
		var runOptions = CreateRunOptions(request.Mode);

		AIAgent agent = chatClient.AsAIAgent(
			instructions: AgentPolicy.Build(request.Mode),
			name: "UnityAgent",
			tools: tools,
			loggerFactory: _loggerFactory);

		var response = new StringBuilder();

		await foreach (var update in agent.RunStreamingAsync(
			context.Prompt,
			options: runOptions,
			cancellationToken: cancellationToken))
		{
			if (string.IsNullOrEmpty(update.Text))
				continue;

			response.Append(update.Text);

			yield return new AgentTextDelta(update.Text);
		}

		_sessionMemory.AddExchange(request.SessionId, request.Message, response.ToString());
	}

	private IList<AITool> CreateTools(AgentRequest request)
	{
		var budget = new ToolCallBudget(_agentSettings.MaximumToolSteps);

		var searchTools = new SearchAgentTools(
			_code,
			_projectIndex,
			_trace,
			request.Workspace,
			budget);

		var codeTools = new CodeAgentTools(
			_code,
			_trace,
			request.Workspace,
			budget);

		var editorTools = new EditorContextAgentTools(
			_code,
			request.Workspace,
			request.Resources);

		IList<AITool> readOnlyTools =
		[
			AIFunctionFactory.Create(
				editorTools.ReadCurrentFileAsync,
				name: "read_current_file"),

			AIFunctionFactory.Create(
				searchTools.FindSymbolAsync,
				name: "find_symbol"),

			AIFunctionFactory.Create(
				searchTools.FindFileAsync,
				name: "find_file"),

			AIFunctionFactory.Create(
				searchTools.FindTextAsync,
				name: "find_text"),

			AIFunctionFactory.Create(
				codeTools.ReadCodeAsync,
				name: "read_code"),

			AIFunctionFactory.Create(
				codeTools.AnalyzeCallsAsync,
				name: "analyze_calls"),

			AIFunctionFactory.Create(
				codeTools.GetFileProblemsAsync,
				name: "get_file_problems")
		];

		return request.Mode switch
		{
			AgentMode.Explain => readOnlyTools,
			AgentMode.Code => readOnlyTools,
			AgentMode.Debug => readOnlyTools,
			_ => throw new ArgumentOutOfRangeException(nameof(request.Mode))
		};
	}

    private static ChatClientAgentRunOptions CreateRunOptions(AgentMode mode)
    {
        var effort = mode switch
        {
            AgentMode.Explain => ReasoningEffort.None,
            AgentMode.Code => ReasoningEffort.Low,
            AgentMode.Debug => ReasoningEffort.Medium,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        return new ChatClientAgentRunOptions(new ChatOptions
        {
            Reasoning = new ReasoningOptions
            {
                Effort = effort
            },
            MaxOutputTokens = 2048
        });
    }

    private ChatClient CreateChatClient(string modelId)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(_settings.ServerUrl.TrimEnd('/') + "/v1")
        };

        return new ChatClient(modelId, new ApiKeyCredential("lm-studio"), options);
    }
}
